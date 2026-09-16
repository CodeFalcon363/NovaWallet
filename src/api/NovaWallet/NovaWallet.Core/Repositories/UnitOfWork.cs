using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class UnitOfWork(NovaWalletDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
