using NovaWallet.Core.Services;

namespace NovaWallet.Api.BackgroundServices;

/// <summary>Thin hosting adapter for IdempotencyCleanupService — hourly is plenty for a 24h-scale TTL.</summary>
public class IdempotencyCleanupBackgroundService(IServiceScopeFactory scopeFactory, ILogger<IdempotencyCleanupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<IdempotencyCleanupService>();
                await cleanup.CleanupExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idempotency key cleanup tick failed");
            }
        }
    }
}
