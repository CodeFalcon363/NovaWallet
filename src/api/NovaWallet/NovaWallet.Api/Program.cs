using Microsoft.EntityFrameworkCore;
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
                .AddNovaWalletRateLimiting();

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

            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>().Database.Migrate();
            }

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

            app.Run();
        }
    }
}
