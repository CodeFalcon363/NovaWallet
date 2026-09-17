using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IWalletRepository
{
    /// <summary>Tracked read — use when the caller intends to mutate the wallet.</summary>
    Task<Wallet?> GetTrackedAsync(Guid walletId, CancellationToken cancellationToken);

    void Add(Wallet wallet);

    /// <summary>Reloads current database values into a tracked entity after a concurrency conflict.</summary>
    Task ReloadAsync(Wallet wallet, CancellationToken cancellationToken);
}
