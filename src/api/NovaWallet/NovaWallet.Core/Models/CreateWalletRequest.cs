using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Models;

public class CreateWalletRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "CustomerId may only contain letters, digits, '-' and '_'.")]
    public string CustomerId { get; set; } = string.Empty;
}
