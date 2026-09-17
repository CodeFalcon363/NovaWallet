using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Services;

/// <summary>
/// Core dispatch logic for the transactional outbox (BRD NFR-ARCH-5), independent of any hosting
/// concern — the ASP.NET Core BackgroundService that calls this on a timer is a thin adapter
/// (NovaWallet.Api/BackgroundServices/OutboxBackgroundService), kept separate so this logic is
/// unit-testable without a real message broker.
/// </summary>
public class OutboxDispatcherService(IOutboxRepository outboxRepository, IEventPublisher eventPublisher, IUnitOfWork unitOfWork)
{
    private const int BatchSize = 20;

    /// <summary>Publishes one batch of unprocessed messages. Returns the number processed.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var messages = await outboxRepository.GetUnprocessedAsync(BatchSize, cancellationToken);
        var processed = 0;

        // Each message's outcome (published vs. still-pending) is tracked on its own already-
        // tracked entity in memory here, then committed in one SaveChangesAsync after the loop —
        // one round trip for the whole batch instead of one per message. This is still safe
        // under a mid-batch crash: any message published before the crash but not yet committed
        // here will be republished on the next tick, which is fine under the at-least-once /
        // idempotent-consumer contract this system already commits to (same principle as the
        // transfer endpoint's Idempotency-Key handling).
        foreach (var message in messages)
        {
            try
            {
                await eventPublisher.PublishAsync(message.Type, message.PayloadJson, message.CorrelationId, cancellationToken);
                message.ProcessedAtUtc = DateTime.UtcNow;
                processed++;
            }
            catch
            {
                message.Attempts += 1;
            }
        }

        if (messages.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }
}
