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
public class TransferServiceTests(SqlServerFixture fixture)
{
    private (WalletService Wallets, TransferService Transfers) CreateServices(NovaWalletDbContext context, string callerCustomerId)
    {
        var walletRepository = new WalletRepository(context);
        var walletQueries = new WalletQueries(fixture.CreateConnectionFactory());
        var ledgerTransactionRepository = new LedgerTransactionRepository(context);
        var auditLogRepository = new AuditLogRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var idempotencyRepository = new IdempotencyRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var callerContext = new FakeCallerContext(callerCustomerId);

        var walletService = new WalletService(walletRepository, walletQueries, ledgerTransactionRepository, auditLogRepository, outboxRepository, unitOfWork, callerContext);
        var transferService = new TransferService(walletRepository, ledgerTransactionRepository, auditLogRepository, outboxRepository, idempotencyRepository, unitOfWork, callerContext);

        return (walletService, transferService);
    }

    private async Task<(Guid SourceWalletId, Guid DestinationWalletId, string CustomerId)> CreateFundedPairAsync(long startingBalanceMinor)
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var otherCustomerId = $"cust-{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        var (wallets, _) = CreateServices(context, customerId);
        var source = await wallets.CreateWalletAsync(new CreateWalletRequest { CustomerId = customerId }, CancellationToken.None);

        await using var otherContext = fixture.CreateDbContext();
        var (otherWallets, _) = CreateServices(otherContext, otherCustomerId);
        var destination = await otherWallets.CreateWalletAsync(new CreateWalletRequest { CustomerId = otherCustomerId }, CancellationToken.None);

        if (startingBalanceMinor > 0)
        {
            await using var creditContext = fixture.CreateDbContext();
            var (creditWallets, _) = CreateServices(creditContext, customerId);
            await creditWallets.CreditAsync(source.WalletId, new CreditWalletRequest { AmountMinor = startingBalanceMinor }, CancellationToken.None);
        }

        return (source.WalletId, destination.WalletId, customerId);
    }

    [Fact]
    public async Task TransferAsync_Moves_Funds_Atomically()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(10_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, customerId);

        var result = await transfers.TransferAsync(
            Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 3_000 },
            CancellationToken.None);

        Assert.Equal(7_000, result.NewSourceBalanceMinor);

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        var destination = await verifyContext.Wallets.FindAsync(destinationId);
        Assert.Equal(7_000, source!.BalanceMinor);
        Assert.Equal(3_000, destination!.BalanceMinor);
    }

    [Fact]
    public async Task TransferAsync_Rejects_Self_Transfer()
    {
        var (sourceId, _, customerId) = await CreateFundedPairAsync(1_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, customerId);

        var exception = await Assert.ThrowsAsync<InvalidTransferException>(() =>
            transfers.TransferAsync(Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = sourceId, AmountMinor = 100 },
                CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task TransferAsync_Rejects_Insufficient_Funds()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(1_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, customerId);

        var exception = await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            transfers.TransferAsync(Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 1_001 },
                CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        Assert.Equal(1_000, source!.BalanceMinor);
    }

    [Fact]
    public async Task TransferAsync_Rejects_When_Caller_Does_Not_Own_Source()
    {
        var (sourceId, destinationId, _) = await CreateFundedPairAsync(1_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, "a-different-customer");

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            transfers.TransferAsync(Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 100 },
                CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task TransferAsync_Replaying_Same_Key_And_Payload_Does_Not_Reprocess()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(10_000);
        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 2_000 };

        await using var context1 = fixture.CreateDbContext();
        var (_, transfers1) = CreateServices(context1, customerId);
        var first = await transfers1.TransferAsync(idempotencyKey, request, CancellationToken.None);

        await using var context2 = fixture.CreateDbContext();
        var (_, transfers2) = CreateServices(context2, customerId);
        var second = await transfers2.TransferAsync(idempotencyKey, request, CancellationToken.None);

        Assert.Equal(first.TransferId, second.TransferId);
        Assert.Equal(first.NewSourceBalanceMinor, second.NewSourceBalanceMinor);

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        Assert.Equal(8_000, source!.BalanceMinor); // debited exactly once, not twice
    }

    [Fact]
    public async Task TransferAsync_Reusing_Key_With_Different_Payload_Is_Rejected()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(10_000);
        var idempotencyKey = Guid.NewGuid().ToString();

        await using var context1 = fixture.CreateDbContext();
        var (_, transfers1) = CreateServices(context1, customerId);
        await transfers1.TransferAsync(idempotencyKey,
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 1_000 },
            CancellationToken.None);

        await using var context2 = fixture.CreateDbContext();
        var (_, transfers2) = CreateServices(context2, customerId);

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            transfers2.TransferAsync(idempotencyKey,
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 9_999 },
                CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task TransferAsync_Concurrent_Replays_Of_Same_New_Key_Process_Exactly_Once()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(10_000);
        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 2_000 };

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = fixture.CreateDbContext();
            var (_, transfers) = CreateServices(context, customerId);
            return await transfers.TransferAsync(idempotencyKey, request, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(results[0].TransferId, r.TransferId));

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        Assert.Equal(8_000, source!.BalanceMinor); // debited exactly once across 8 concurrent replays
    }

    [Fact]
    public async Task TransferAsync_Under_Concurrent_Load_Never_Overdraws_Source_Wallet()
    {
        const long startingBalance = 10_000;
        const long amountPerTransfer = 1_000;
        const int concurrentRequests = 20; // more than the wallet can possibly fund (20 * 1000 > 10000)

        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(startingBalance);

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var context = fixture.CreateDbContext();
            var (_, transfers) = CreateServices(context, customerId);
            try
            {
                await transfers.TransferAsync(
                    Guid.NewGuid().ToString(),
                    new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = amountPerTransfer },
                    CancellationToken.None);
                return true;
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        });

        var outcomes = await Task.WhenAll(tasks);
        var successCount = outcomes.Count(success => success);

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        var destination = await verifyContext.Wallets.FindAsync(destinationId);

        // The core invariant: balance never goes negative, no matter how many concurrent
        // requests raced for it.
        Assert.True(source!.BalanceMinor >= 0);

        // Exactly as many transfers as the starting balance could fund actually succeeded —
        // no double-spend (more successes than the balance allows) and no lost updates
        // (fewer successes than the balance allows).
        var expectedSuccesses = (int)(startingBalance / amountPerTransfer);
        Assert.Equal(expectedSuccesses, successCount);

        // Money is conserved: nothing was created or destroyed by the concurrent contention.
        Assert.Equal(startingBalance, source.BalanceMinor + destination!.BalanceMinor);
        Assert.Equal(startingBalance - successCount * amountPerTransfer, source.BalanceMinor);
    }
}
