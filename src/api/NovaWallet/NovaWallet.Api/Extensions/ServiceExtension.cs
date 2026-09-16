using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api.Security;
using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Queries;
using NovaWallet.Core.Repositories;
using NovaWallet.Core.Services;

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
            options.UseSqlServer(GetConnectionString(sp)));

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
        return services;
    }

    public static IServiceCollection AddNovaWalletApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<WalletService>();
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
}
