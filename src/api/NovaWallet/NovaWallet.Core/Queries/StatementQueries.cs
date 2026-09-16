using Dapper;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Models;

namespace NovaWallet.Core.Queries;

public class StatementQueries(ISqlConnectionFactory connectionFactory) : IStatementQueries
{
    private record Row(Guid TransactionId, string Type, long AmountMinor, long BalanceAfterMinor, Guid? CounterpartyWalletId, DateTime CreatedAtUtc, int TotalCount);

    public async Task<PagedResult<StatementEntryResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // COUNT(*) OVER() piggybacks the total row count onto the same query as the page of
        // rows — one round trip instead of a separate COUNT query (indexed on WalletId+CreatedAt).
        const string sql = """
            SELECT TransactionId, Type, AmountMinor, BalanceAfterMinor, CounterpartyWalletId, CreatedAtUtc,
                   COUNT(*) OVER() AS TotalCount
            FROM LedgerTransactions
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
            .Select(r => new StatementEntryResponse(r.TransactionId, r.Type, r.AmountMinor, r.BalanceAfterMinor, r.CounterpartyWalletId, r.CreatedAtUtc))
            .ToList();

        var totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        return new PagedResult<StatementEntryResponse>(items, page, pageSize, totalCount);
    }
}
