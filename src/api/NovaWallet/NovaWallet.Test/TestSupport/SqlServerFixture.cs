using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;
using Xunit;

namespace NovaWallet.Test.TestSupport;

/// <summary>
/// Real SQL Server test database, shared across a test collection. Using the real engine — not
/// SQLite/InMemory — matters here: unique-constraint exception translation and
/// optimistic-concurrency (RowVersion) behavior only match production against a real SQL Server
/// engine.
///
/// Defaults to LocalDB (Windows-only, ships with Visual Studio). On macOS/Linux/CI, or to test
/// against the docker-compose SQL Server instead, set NOVAWALLET_TEST_SQL_BASE to a connection
/// string with no Database= segment, e.g.:
///   Server=localhost,1433;User Id=sa;Password=NovaWallet_Dev_Pw1!;TrustServerCertificate=True;
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private const string DefaultBaseConnectionString = "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly string _databaseName = $"NovaWalletTest_{Guid.NewGuid():N}";

    public string ConnectionString
    {
        get
        {
            var baseConnectionString = Environment.GetEnvironmentVariable("NOVAWALLET_TEST_SQL_BASE") ?? DefaultBaseConnectionString;
            return $"{baseConnectionString};Database={_databaseName};";
        }
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
    }

    public NovaWalletDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new NovaWalletDbContext(options);
    }

    public ISqlConnectionFactory CreateConnectionFactory() => new SqlConnectionFactory(ConnectionString);
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
