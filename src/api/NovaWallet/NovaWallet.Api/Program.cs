using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NovaWallet.Api.Extensions;
using NovaWallet.Api.Middleware;
using NovaWallet.Core.Data;

namespace NovaWallet.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Server disclosure (checklist item): Kestrel adds a "Server: Kestrel" response
            // header by default; suppress it rather than advertise the stack to callers.
            builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

            builder.Services
                .AddNovaWalletPersistence()
                .AddNovaWalletRepositories()
                .AddNovaWalletApplicationServices()
                .AddNovaWalletJwtAuthentication()
                .AddNovaWalletOutboxDispatch()
                .AddNovaWalletIdempotencyCleanup()
                .AddNovaWalletRateLimiting()
                .AddNovaWalletHealthChecks();

            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<NovaWalletExceptionHandler>();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT bearer token, e.g. from POST /auth/tokens",
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                        Array.Empty<string>()
                    },
                });
            });

            var app = builder.Build();

            MigrateDatabaseWithRetry(app);

            // Runs first so the correlation ID is available to the exception handler and every
            // downstream log/audit/outbox write (NFR-OBS-2).
            app.UseMiddleware<CorrelationIdMiddleware>();

            app.UseExceptionHandler();
            app.UseHsts();

            // Always reachable, not just in Development: the task brief requires the
            // OpenAPI/Swagger spec to be reachable when the service is running, and
            // docker-compose does not set ASPNETCORE_ENVIRONMENT=Development.
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();

            app.UseMiddleware<RequestResponseLoggingMiddleware>();

            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            // Liveness: the process is up, no dependency checks (fast, for orchestrator restarts).
            app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
            // Readiness: gates traffic on SQL Server + RabbitMQ actually being reachable.
            app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

            app.Run();
        }

        /// <summary>
        /// One-time startup gate: docker-compose's healthcheck only proves SQL Server accepted a
        /// TCP connection for `sqlcmd`, not that it's ready for every connection this process
        /// opens a moment later. EnableRetryOnFailure (see AddNovaWalletPersistence) covers
        /// transient errors during normal operation; this covers the same class of failure for
        /// the one call — Migrate() — that runs before the app can serve any request at all.
        /// </summary>
        private static void MigrateDatabaseWithRetry(WebApplication app)
        {
            const int maxAttempts = 10;
            var delay = TimeSpan.FromSeconds(3);
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var scope = app.Services.CreateScope();
                    scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>().Database.Migrate();
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    logger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}s", attempt, maxAttempts, delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }
        }
    }
}
