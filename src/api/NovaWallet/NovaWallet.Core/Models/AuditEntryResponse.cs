namespace NovaWallet.Core.Models;

public record AuditEntryResponse(
    Guid AuditId,
    string Action,
    string ActorId,
    long? BalanceBeforeMinor,
    long? BalanceAfterMinor,
    string IpAddress,
    DateTime CreatedAtUtc);
