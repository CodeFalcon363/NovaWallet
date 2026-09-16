using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Models;

public class TransferRequest
{
    [Required]
    public Guid SourceWalletId { get; set; }

    [Required]
    public Guid DestinationWalletId { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "AmountMinor must be a positive integer number of kobo.")]
    public long AmountMinor { get; set; }
}
