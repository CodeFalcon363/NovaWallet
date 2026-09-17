using Dapper;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Models;

namespace NovaWallet.Core.Queries;

public class AuditQueries(ISqlConnectionFactory connectionFactory) : IAuditQueries
{
    private record Row(Guid AuditId, string Action, string ActorId, long? BalanceBeforeMinor, long? BalanceAfterMinor, string IpAddress, DateTime CreatedAtUtc, int TotalCount);

    public async Task<PagedResult<AuditEntryResponse>> GetAuditTrailAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT AuditId, Action, ActorId, BalanceBeforeMinor, BalanceAfterMinor, IpAddress, CreatedAtUtc,
                   COUNT(*) OVER() AS TotalCount
            FROM AuditLogEntries
            WHERE WalletId = @WalletId
            ORDER BY CreatedAtUtc DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var command = new CommandDefinition(
            sql,
            new { WalletId = walletId, Offset = (page - 1) * pageSize, PageSize = pageSize },
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<Row>(command)).ToList();

        var items = rows
            .Select(r => new AuditEntryResponse(r.AuditId, r.Action, r.ActorId, r.BalanceBeforeMinor, r.BalanceAfterMinor, r.IpAddress, r.CreatedAtUtc))
            .ToList();

        var totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        return new PagedResult<AuditEntryResponse>(items, page, pageSize, totalCount);
    }
}
