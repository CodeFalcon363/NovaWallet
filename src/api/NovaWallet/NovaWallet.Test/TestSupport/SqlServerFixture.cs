using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;
using Xunit;

namespace NovaWallet.Test.TestSupport;

/// <summary>
/// Real SQL Server (via LocalDB) test database, shared across a test collection. Using the
/// real engine — not SQLite/InMemory — matters here: unique-constraint exception translation
/// and optimistic-concurrency (RowVersion) behavior only match production against a real
/// SQL Server engine.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"NovaWalletTest_{Guid.NewGuid():N}";

    public string ConnectionString => $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

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
