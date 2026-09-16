using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Models;

public class CreditWalletRequest
{
    [Range(1, long.MaxValue, ErrorMessage = "AmountMinor must be a positive integer number of kobo.")]
    public long AmountMinor { get; set; }
}
