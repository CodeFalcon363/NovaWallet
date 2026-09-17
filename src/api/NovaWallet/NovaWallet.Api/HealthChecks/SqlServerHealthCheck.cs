using Microsoft.Extensions.Diagnostics.HealthChecks;
using NovaWallet.Core.Data;

namespace NovaWallet.Api.HealthChecks;

public class SqlServerHealthCheck(NovaWalletDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQL Server is reachable.")
                : HealthCheckResult.Unhealthy("SQL Server is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server health check threw an exception.", ex);
        }
    }
}
