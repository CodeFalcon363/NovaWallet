using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

public class Wallet
{
    public Guid WalletId { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string CustomerId { get; set; } = string.Empty;

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "NGN";

    [Range(0, long.MaxValue)]
    public long BalanceMinor { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
