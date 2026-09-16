using System.Data;

namespace NovaWallet.Core.Interfaces;

/// <summary>Read-side (Dapper) connection source — independent of the EF Core write-side DbContext.</summary>
public interface ISqlConnectionFactory
{
    IDbConnection CreateOpenConnection();
}
