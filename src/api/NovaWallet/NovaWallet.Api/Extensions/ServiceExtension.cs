using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api.BackgroundServices;
using NovaWallet.Api.HealthChecks;
using NovaWallet.Api.Security;
using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Queries;
using NovaWallet.Core.Repositories;
using NovaWallet.Core.Services;
using NovaWallet.Infrastructure.ExternalServices;
using RedisRateLimiting;
using StackExchange.Redis;

namespace NovaWallet.Api.Extensions;

public static class ServiceExtension
{
    private const string ConnectionStringName = "NovaWalletDb";

    /// <summary>
    /// Resolves the connection string lazily from IConfiguration at service-resolution time
    /// (not at registration time) — needed so a test host's post-build configuration override
    /// (e.g. WebApplicationFactory) is honored instead of whatever was captured eagerly.
    /// </summary>
    public static IServiceCollection AddNovaWalletPersistence(this IServiceCollection services)
    {
        services.AddDbContext<NovaWalletDbContext>((sp, options) =>
            options.UseSqlServer(GetConnectionString(sp), sqlOptions =>
                // A container-healthy SQL Server doesn't guarantee every subsequent connection
                // attempt succeeds instantly — transient errors (brief network blips, SQL Server
                // still warming up its login subsystem right after docker-compose's healthcheck
                // passes) get retried transparently instead of surfacing as a hard failure.
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

        services.AddSingleton<ISqlConnectionFactory>(sp =>
            new SqlConnectionFactory(GetConnectionString(sp)));

        return services;
    }

    private static string GetConnectionString(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Missing ConnectionStrings:{ConnectionStringName} configuration.");

    public static IServiceCollection AddNovaWalletRepositories(this IServiceCollection services)
    {
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ILedgerTransactionRepository, LedgerTransactionRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IDailyUsageRepository, DailyUsageRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IWalletQueries, WalletQueries>();
        services.AddScoped<IStatementQueries, StatementQueries>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        return services;
    }

    public static IServiceCollection AddNovaWalletApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<DailyOutboundLimitOptions>().BindConfiguration(DailyOutboundLimitOptions.SectionName);
        services.AddOptions<IdempotencyKeyTtlOptions>().BindConfiguration(IdempotencyKeyTtlOptions.SectionName);

        services.AddScoped<WalletService>();
        services.AddScoped<TransferService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICallerContext, HttpCallerContext>();
        services.AddScoped<DevTokenIssuer>();
        return services;
    }

    /// <summary>Configure callbacks for named options resolve lazily (first use, post-build), so
    /// this also correctly picks up test-time configuration overrides.</summary>
    public static IServiceCollection AddNovaWalletJwtAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var options = jwtOptions.Value;
                // Keep claim types exactly as issued (e.g. "sub") — without this, ASP.NET Core
                // remaps "sub" to ClaimTypes.NameIdentifier, silently breaking claim lookups
                // that search for JwtRegisteredClaimNames.Sub (see HttpCallerContext.ActorId).
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddNovaWalletOutboxDispatch(this IServiceCollection services)
    {
        services.AddOptions<RabbitMqOptions>().BindConfiguration(RabbitMqOptions.SectionName);
        services.AddSingleton<IEventPublisher, RabbitMqPublisher>();
        services.AddScoped<OutboxDispatcherService>();
        services.AddHostedService<OutboxBackgroundService>();
        return services;
    }

    /// <summary>Bounds idempotency-record storage growth by purging expired, non-Pending rows.</summary>
    public static IServiceCollection AddNovaWalletIdempotencyCleanup(this IServiceCollection services)
    {
        services.AddScoped<IdempotencyCleanupService>();
        services.AddHostedService<IdempotencyCleanupBackgroundService>();
        return services;
    }

    /// <summary>
    /// Rate limiting on the transfer endpoint (checklist item; NFR-SEC-7) — backed by Redis, not
    /// ASP.NET Core's in-memory limiter. An in-memory limiter counts requests per *process*: with
    /// N horizontally-scaled instances behind a load balancer, the effective global limit becomes
    /// N × the configured limit, not the configured limit, because each instance keeps its own
    /// independent counter. A shared Redis-backed limiter (via the RedisRateLimiting library — a
    /// sliding-window Lua script under the hood, not hand-rolled here) enforces one true global
    /// count across every instance. Sliding window specifically (not fixed window) also closes
    /// the boundary-burst gap: a fixed window resets abruptly, so a client can send the full
    /// permit count in the last moment of one window and again in the first moment of the next,
    /// briefly doubling the intended rate.
    /// </summary>
    public static IServiceCollection AddNovaWalletRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RedisOptions>().BindConfiguration(RedisOptions.SectionName);

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimiterPolicies.Transfer, httpContext =>
            {
                var connectionMultiplexer = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();

                // One shared partition key ("global"), not per-caller: the limit is on the
                // endpoint as a whole, matching the brief's "rate-limiting middleware on the
                // transfer endpoint" — not a per-user quota.
                return RedisRateLimitPartition.GetSlidingWindowRateLimiter("global", _ => new RedisSlidingWindowRateLimiterOptions
                {
                    ConnectionMultiplexerFactory = () => connectionMultiplexer,
                    PermitLimit = 20,
                    Window = TimeSpan.FromSeconds(10),
                });
            });

            // RFC 7807 even on rate-limit rejection, consistent with every other error response.
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many transfer requests. Please retry shortly.",
                };
                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

        return services;
    }

    /// <summary>Liveness/readiness endpoints suitable for container orchestration (NFR-OBS-3).</summary>
    public static IServiceCollection AddNovaWalletHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }
}
