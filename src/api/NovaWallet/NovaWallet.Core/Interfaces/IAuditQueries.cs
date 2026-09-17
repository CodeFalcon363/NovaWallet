using NovaWallet.Core.Models;

namespace NovaWallet.Core.Interfaces;

/// <summary>
/// Read side (Dapper) for the append-only audit trail — separate from the statement/transaction
/// table judges can query (task brief §2.1 "Audit log").
/// </summary>
public interface IAuditQueries
{
    Task<PagedResult<AuditEntryResponse>> GetAuditTrailAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken);
}
