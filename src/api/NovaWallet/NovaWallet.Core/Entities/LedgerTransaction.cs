using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

public class LedgerTransaction
{
    public Guid TransactionId { get; set; }

    public Guid WalletId { get; set; }

    public LedgerTransactionType Type { get; set; }

    [Range(1, long.MaxValue)]
    public long AmountMinor { get; set; }

    [Range(0, long.MaxValue)]
    public long BalanceAfterMinor { get; set; }

    public Guid? CounterpartyWalletId { get; set; }

    [StringLength(128)]
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
