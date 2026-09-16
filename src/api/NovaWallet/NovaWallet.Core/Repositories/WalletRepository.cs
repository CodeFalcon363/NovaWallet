using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class WalletRepository(NovaWalletDbContext context) : IWalletRepository
{
    public Task<Wallet?> GetTrackedAsync(Guid walletId, CancellationToken cancellationToken) =>
        context.Wallets.FirstOrDefaultAsync(w => w.WalletId == walletId, cancellationToken);

    public void Add(Wallet wallet) => context.Wallets.Add(wallet);

    public Task ReloadAsync(Wallet wallet, CancellationToken cancellationToken) =>
        context.Entry(wallet).ReloadAsync(cancellationToken);
}
