namespace NovaWallet.Core.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Detaches all tracked entities, discarding any pending (unsaved) changes. Used between
    /// optimistic-concurrency retry attempts so the next attempt re-reads fresh state instead
    /// of reusing a stale tracked entity or re-submitting the same failed change.
    /// </summary>
    void ResetTracking();
}
