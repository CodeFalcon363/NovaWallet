using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Services;

/// <summary>
/// Core dispatch logic for the transactional outbox (BRD NFR-ARCH-5), independent of any hosting
/// concern — the ASP.NET Core BackgroundService that calls this on a timer is a thin adapter
/// (NovaWallet.Api/BackgroundServices/OutboxBackgroundService), kept separate so this logic is
/// unit-testable without a real message broker.
/// </summary>
public class OutboxDispatcherService(IOutboxRepository outboxRepository, IEventPublisher eventPublisher)
{
    private const int BatchSize = 20;

    /// <summary>Publishes one batch of unprocessed messages. Returns the number processed.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var messages = await outboxRepository.GetUnprocessedAsync(BatchSize, cancellationToken);
        var processed = 0;

        foreach (var message in messages)
        {
            try
            {
                await eventPublisher.PublishAsync(message.Type, message.PayloadJson, message.CorrelationId, cancellationToken);
                await outboxRepository.MarkProcessedAsync(message.OutboxMessageId, cancellationToken);
                processed++;
            }
            catch
            {
                // At-least-once delivery: leave the row unprocessed for the next pass. Consumers
                // of these events must be idempotent, same principle as the transfer endpoint's
                // Idempotency-Key handling.
                await outboxRepository.IncrementAttemptsAsync(message.OutboxMessageId, cancellationToken);
            }
        }

        return processed;
    }
}
