using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    /// <summary>
    /// Tracked entities — the dispatcher mutates ProcessedAtUtc/Attempts on the returned
    /// instances directly and commits the whole batch in one IUnitOfWork.SaveChangesAsync call,
    /// rather than one repository call (and one round trip) per message.
    /// </summary>
    Task<List<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken);
}
