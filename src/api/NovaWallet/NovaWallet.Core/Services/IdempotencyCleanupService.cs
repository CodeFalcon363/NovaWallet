using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Services;

/// <summary>
/// Purges expired idempotency records to bound storage growth (BRD/README: TTL). Never deletes
/// Pending rows — see TransferService's decision table for why an "expired" Pending record is
/// left alone rather than treated as abandoned.
/// </summary>
public class IdempotencyCleanupService(IIdempotencyRepository idempotencyRepository, TimeProvider timeProvider)
{
    public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) =>
        idempotencyRepository.DeleteExpiredAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
}
