namespace NovaWallet.Core.Interfaces;

public record WalletBalanceView(Guid WalletId, string CustomerId, long BalanceMinor, string Currency);

/// <summary>Read side (Dapper) — CQRS-lite split, see BRD NFR-ARCH-3.</summary>
public interface IWalletQueries
{
    Task<WalletBalanceView?> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken);
}
