using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NovaWallet.Infrastructure.ExternalServices;
using RabbitMQ.Client;

namespace NovaWallet.Api.HealthChecks;

public class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = config.HostName,
                Port = config.Port,
                UserName = config.UserName,
                Password = config.Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
            };

            using var connection = factory.CreateConnection("novawallet-health-check");
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ is reachable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", ex));
        }
    }
}
