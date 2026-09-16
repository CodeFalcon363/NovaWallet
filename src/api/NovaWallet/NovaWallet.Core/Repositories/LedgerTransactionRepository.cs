using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class LedgerTransactionRepository(NovaWalletDbContext context) : ILedgerTransactionRepository
{
    public void Add(LedgerTransaction transaction) => context.LedgerTransactions.Add(transaction);
}
