using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IIdempotencyRepository
{
    /// <summary>Fresh, uncached read — see IdempotencyRepository.GetAsync for why this must never use FindAsync.</summary>
    Task<TransferIdempotencyRecord?> GetAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to reserve a brand-new Pending record for a key with no existing row. Returns
    /// true if this call created it (caller proceeds to process the transfer); false if another
    /// request won the race to insert it first (caller re-checks via GetAsync).
    /// </summary>
    Task<bool> TryReserveAsync(string idempotencyKey, string requestFingerprint, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims an existing row for a retry (Failed record, or an expired Completed
    /// record) by transitioning it back to Pending, guarded by <paramref name="expectedRowVersion"/>
    /// so two concurrent retries of the same key can't both proceed — exactly the race that made
    /// this method necessary (see TransferService for the decision table). Returns false if
    /// another request already claimed it first.
    /// </summary>
    Task<bool> TryClaimForRetryAsync(string idempotencyKey, string requestFingerprint, DateTime expiresAtUtc, byte[] expectedRowVersion, CancellationToken cancellationToken);

    Task MarkCompletedAsync(string idempotencyKey, Guid resultTransactionId, CancellationToken cancellationToken);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Bulk-deletes expired, non-Pending records to bound storage growth. Never touches Pending rows.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
