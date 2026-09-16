using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface ILedgerTransactionRepository
{
    void Add(LedgerTransaction transaction);

    /// <summary>Used to reconstruct the response when replaying a completed idempotent transfer.</summary>
    Task<LedgerTransaction?> GetAsync(Guid transactionId, CancellationToken cancellationToken);
}
