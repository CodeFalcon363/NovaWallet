using Dapper;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Queries;

public class WalletQueries(ISqlConnectionFactory connectionFactory) : IWalletQueries
{
    public async Task<WalletBalanceView?> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT WalletId, CustomerId, BalanceMinor, Currency
            FROM Wallets
            WHERE WalletId = @WalletId
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var command = new CommandDefinition(sql, new { WalletId = walletId }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<WalletBalanceView>(command);
    }
}
