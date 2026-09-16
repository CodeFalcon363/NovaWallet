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
        var ledgerTransactionRepository = new LedgerTransactionRepository(context);
        var auditLogRepository = new AuditLogRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var callerContext = new FakeCallerContext(callerCustomerId);

        return new WalletService(walletRepository, walletQueries, ledgerTransactionRepository, auditLogRepository, outboxRepository, unitOfWork, callerContext);
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

    [Fact]
    public async Task CreditAsync_Increases_Balance_And_Writes_Ledger_And_Audit()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        Guid walletId;

        await using (var createContext = fixture.CreateDbContext())
        {
            var createService = CreateService(createContext, customerId);
            var created = await createService.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);
            walletId = created.WalletId;
        }

        await using var creditContext = fixture.CreateDbContext();
        var creditService = CreateService(creditContext, customerId);

        var result = await creditService.CreditAsync(walletId, new CreditWalletRequest { AmountMinor = 50_000 }, CancellationToken.None);

        Assert.Equal(50_000, result.BalanceMinor);

        await using var verifyContext = fixture.CreateDbContext();
        var ledgerEntry = Assert.Single(verifyContext.LedgerTransactions, t => t.WalletId == walletId);
        Assert.Equal(NovaWallet.Core.Entities.LedgerTransactionType.Credit, ledgerEntry.Type);
        Assert.Equal(50_000, ledgerEntry.AmountMinor);
        Assert.Equal(50_000, ledgerEntry.BalanceAfterMinor);

        var auditEntry = Assert.Single(verifyContext.AuditLogEntries, a => a.WalletId == walletId && a.Action == "BalanceCredited");
        Assert.Equal(0, auditEntry.BalanceBeforeMinor);
        Assert.Equal(50_000, auditEntry.BalanceAfterMinor);
    }

    [Fact]
    public async Task CreditAsync_Throws_NotFound_For_Unknown_Wallet()
    {
        await using var context = fixture.CreateDbContext();
        var service = CreateService(context, "some-customer");

        var exception = await Assert.ThrowsAsync<WalletNotFoundException>(() =>
            service.CreditAsync(Guid.NewGuid(), new CreditWalletRequest { AmountMinor = 100 }, CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task CreditAsync_Throws_Forbidden_When_Caller_Is_Not_Owner()
    {
        var ownerCustomerId = $"cust-{Guid.NewGuid():N}";
        Guid walletId;

        await using (var createContext = fixture.CreateDbContext())
        {
            var createService = CreateService(createContext, ownerCustomerId);
            var created = await createService.CreateWalletAsync(new CreateWalletRequest { CustomerId = ownerCustomerId }, CancellationToken.None);
            walletId = created.WalletId;
        }

        await using var creditContext = fixture.CreateDbContext();
        var otherService = CreateService(creditContext, "a-different-customer");

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            otherService.CreditAsync(walletId, new CreditWalletRequest { AmountMinor = 100 }, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task CreditAsync_Under_Concurrent_Load_Sums_Correctly_No_Lost_Updates()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        Guid walletId;

        await using (var createContext = fixture.CreateDbContext())
        {
            var createService = CreateService(createContext, customerId);
            var created = await createService.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);
            walletId = created.WalletId;
        }

        const int concurrentRequests = 20;
        const long amountPerCredit = 1_000;

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            // Each simulated concurrent request gets its own DbContext, exactly as a real HTTP
            // request would via per-scope DI — this is what actually exercises the retry loop.
            await using var context = fixture.CreateDbContext();
            var service = CreateService(context, customerId);
            await service.CreditAsync(walletId, new CreditWalletRequest { AmountMinor = amountPerCredit }, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        await using var verifyContext = fixture.CreateDbContext();
        var wallet = await verifyContext.Wallets.FindAsync(walletId);
        Assert.Equal(concurrentRequests * amountPerCredit, wallet!.BalanceMinor);

        var ledgerCount = verifyContext.LedgerTransactions.Count(t => t.WalletId == walletId);
        Assert.Equal(concurrentRequests, ledgerCount);
    }
}
