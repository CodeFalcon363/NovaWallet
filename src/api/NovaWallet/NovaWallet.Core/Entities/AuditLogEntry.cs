using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

/// <summary>
/// Append-only audit trail: who did what, when, balance before/after, and the caller's session IP.
/// No update/delete path is exposed anywhere in the repository layer (see IAuditLogRepository).
/// </summary>
public class AuditLogEntry
{
    public Guid AuditId { get; set; }

    public Guid WalletId { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string ActorId { get; set; } = string.Empty;

    public long? BalanceBeforeMinor { get; set; }

    public long? BalanceAfterMinor { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string IpAddress { get; set; } = string.Empty;

    [StringLength(64)]
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
