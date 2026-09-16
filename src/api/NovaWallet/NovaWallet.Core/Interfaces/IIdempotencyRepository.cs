using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IIdempotencyRepository
{
    Task<TransferIdempotencyRecord?> GetAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to reserve a new Pending record for the key. Returns true if this call created
    /// it (caller proceeds to process the transfer); false if a record already existed (caller
    /// inspects it via GetAsync to decide replay/conflict/in-flight handling).
    /// </summary>
    Task<bool> TryReserveAsync(string idempotencyKey, string requestFingerprint, CancellationToken cancellationToken);

    Task MarkCompletedAsync(string idempotencyKey, Guid resultTransactionId, CancellationToken cancellationToken);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken);
}
