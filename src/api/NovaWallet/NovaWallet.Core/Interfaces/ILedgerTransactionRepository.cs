using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface ILedgerTransactionRepository
{
    void Add(LedgerTransaction transaction);
}
