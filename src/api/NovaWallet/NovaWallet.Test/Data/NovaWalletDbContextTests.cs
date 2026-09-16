using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using Xunit;

namespace NovaWallet.Test.Data;

public class NovaWalletDbContextTests
{
    private static NovaWalletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseSqlServer("Server=(local);Database=NovaWalletModelCheck;Trusted_Connection=True;")
            .Options;
        return new NovaWalletDbContext(options);
    }

    [Fact]
    public void Model_Builds_Without_Error()
    {
        using var context = CreateContext();

        var model = context.Model;

        Assert.NotNull(model);
    }

    [Fact]
    public void Wallet_CustomerId_Has_Unique_Index()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Wallet))!;
        var index = entityType.GetIndexes().Single(i => i.Properties.Single().Name == nameof(Wallet.CustomerId));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void DailyOutboundUsage_Has_Composite_Key_Of_WalletId_And_UsageDate()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(DailyOutboundUsage))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray();

        Assert.Equal([nameof(DailyOutboundUsage.WalletId), nameof(DailyOutboundUsage.UsageDateWat)], keyProperties);
    }

    [Fact]
    public void TransferIdempotencyRecord_Key_Is_IdempotencyKey()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(TransferIdempotencyRecord))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray();

        Assert.Equal([nameof(TransferIdempotencyRecord.IdempotencyKey)], keyProperties);
    }
}
