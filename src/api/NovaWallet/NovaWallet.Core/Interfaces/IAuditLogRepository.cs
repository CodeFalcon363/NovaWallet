using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IAuditLogRepository
{
    /// <summary>Append-only: intentionally no Update/Delete method exists on this interface.</summary>
    void Append(AuditLogEntry entry);
}
