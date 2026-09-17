using NovaWallet.Core.Services;

namespace NovaWallet.Api.BackgroundServices;

/// <summary>
/// Thin hosting adapter: creates a DI scope per tick and delegates to OutboxDispatcherService,
/// which holds the actual dispatch logic and is unit-testable on its own.
/// </summary>
public class OutboxBackgroundService(IServiceScopeFactory scopeFactory, ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcherService>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broker outage or transient failure here must not crash the API host — the
                // outbox rows stay unprocessed and are retried on the next tick.
                logger.LogError(ex, "Outbox dispatch tick failed");
            }
        }
    }
}
