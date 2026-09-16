using System.Data;
using Microsoft.Data.SqlClient;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Data;

public class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public IDbConnection CreateOpenConnection()
    {
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}
