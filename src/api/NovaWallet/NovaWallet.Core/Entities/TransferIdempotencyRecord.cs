using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

public enum IdempotencyStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}

public class TransferIdempotencyRecord
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string RequestFingerprint { get; set; } = string.Empty;

    public IdempotencyStatus Status { get; set; }

    public Guid? ResultTransactionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
