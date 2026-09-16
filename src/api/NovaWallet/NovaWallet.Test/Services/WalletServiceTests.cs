using NovaWallet.Core.Data;
using NovaWallet.Core.Exceptions;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Models;
using NovaWallet.Core.Queries;
using NovaWallet.Core.Repositories;
using NovaWallet.Core.Services;
using NovaWallet.Test.TestSupport;
using Xunit;

namespace NovaWallet.Test.Services;

[Collection(SqlServerCollection.Name)]
public class WalletServiceTests(SqlServerFixture fixture)
{
    private WalletService CreateService(NovaWalletDbContext context, string callerCustomerId)
    {
        var walletRepository = new WalletRepository(context);
        var walletQueries = new WalletQueries(fixture.CreateConnectionFactory());
        var auditLogRepository = new AuditLogRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var callerContext = new FakeCallerContext(callerCustomerId);

        return new WalletService(walletRepository, walletQueries, auditLogRepository, outboxRepository, unitOfWork, callerContext);
    }

    [Fact]
    public async Task CreateWalletAsync_Creates_Wallet_With_Zero_Balance()
    {
        await using var context = fixture.CreateDbContext();
        var customerId = $"cust-{Guid.NewGuid():N}";
        var service = CreateService(context, customerId);

        var result = await service.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.WalletId);
        Assert.Equal(0, result.BalanceMinor);
        Assert.Equal("NGN", result.Currency);
    }

    [Fact]
    public async Task CreateWalletAsync_Writes_Audit_Entry_And_Outbox_Message()
    {
        await using var context = fixture.CreateDbContext();
        var customerId = $"cust-{Guid.NewGuid():N}";
        var service = CreateService(context, customerId);

        var result = await service.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        var auditEntry = Assert.Single(verifyContext.AuditLogEntries, a => a.WalletId == result.WalletId);
        Assert.Equal("WalletCreated", auditEntry.Action);
        Assert.Equal(customerId, auditEntry.ActorId);
        Assert.Null(auditEntry.BalanceBeforeMinor);
        Assert.Equal(0, auditEntry.BalanceAfterMinor);

        Assert.Contains(verifyContext.OutboxMessages, m => m.Type == "WalletCreated" && m.ProcessedAtUtc == null);
    }

    [Fact]
    public async Task CreateWalletAsync_Rejects_Duplicate_CustomerId()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";

        await using (var firstContext = fixture.CreateDbContext())
        {
            var firstService = CreateService(firstContext, customerId);
            await firstService.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);
        }

        await using var secondContext = fixture.CreateDbContext();
        var secondService = CreateService(secondContext, customerId);

        var exception = await Assert.ThrowsAsync<DuplicateWalletException>(() =>
            secondService.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CreateWalletAsync_Rejects_When_CustomerId_Does_Not_Match_Caller()
    {
        await using var context = fixture.CreateDbContext();
        var service = CreateService(context, callerCustomerId: "caller-1");

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateWalletAsync(new CreateWalletRequest { CustomerId = "someone-else" }, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task GetBalanceAsync_Returns_Balance_For_Owner()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        Guid walletId;

        await using (var createContext = fixture.CreateDbContext())
        {
            var createService = CreateService(createContext, customerId);
            var created = await createService.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);
            walletId = created.WalletId;
        }

        await using var readContext = fixture.CreateDbContext();
        var readService = CreateService(readContext, customerId);

        var result = await readService.GetBalanceAsync(walletId, CancellationToken.None);

        Assert.Equal(walletId, result.WalletId);
        Assert.Equal(0, result.BalanceMinor);
    }

    [Fact]
    public async Task GetBalanceAsync_Throws_NotFound_For_Unknown_Wallet()
    {
        await using var context = fixture.CreateDbContext();
        var service = CreateService(context, "some-customer");

        var exception = await Assert.ThrowsAsync<WalletNotFoundException>(() =>
            service.GetBalanceAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task GetBalanceAsync_Throws_Forbidden_When_Caller_Is_Not_Owner()
    {
        var ownerCustomerId = $"cust-{Guid.NewGuid():N}";
        Guid walletId;

        await using (var createContext = fixture.CreateDbContext())
        {
            var createService = CreateService(createContext, ownerCustomerId);
            var created = await createService.CreateWalletAsync(new CreateWalletRequest { CustomerId = ownerCustomerId }, CancellationToken.None);
            walletId = created.WalletId;
        }

        await using var readContext = fixture.CreateDbContext();
        var otherService = CreateService(readContext, "a-different-customer");

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            otherService.GetBalanceAsync(walletId, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
    }
}
