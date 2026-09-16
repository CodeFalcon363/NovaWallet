using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    Task<List<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid outboxMessageId, CancellationToken cancellationToken);

    Task IncrementAttemptsAsync(Guid outboxMessageId, CancellationToken cancellationToken);
}
