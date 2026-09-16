using NovaWallet.Core.Models;

namespace NovaWallet.Core.Interfaces;

/// <summary>Read side (Dapper) — CQRS-lite split, see BRD NFR-ARCH-3.</summary>
public interface IStatementQueries
{
    Task<PagedResult<StatementEntryResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken);
}
