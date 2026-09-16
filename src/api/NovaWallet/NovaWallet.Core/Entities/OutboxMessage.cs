using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

/// <summary>
/// Transactional outbox row, written in the same DB transaction as the business mutation
/// it describes. A background dispatcher publishes unprocessed rows to the message queue.
/// </summary>
public class OutboxMessage
{
    public Guid OutboxMessageId { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Type { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    [StringLength(64)]
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    [Range(0, int.MaxValue)]
    public int Attempts { get; set; }
}
