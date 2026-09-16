using Microsoft.Extensions.Options;
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
    public const long DefaultDailyLimitMinor = 50_000_000; // ₦500,000, matches the production default.

    private (WalletService Wallets, TransferService Transfers) CreateServices(
        NovaWalletDbContext context,
        string callerCustomerId,
        TimeProvider? timeProvider = null,
        long dailyLimitMinor = DefaultDailyLimitMinor)
    {
        var walletRepository = new WalletRepository(context);
        var walletQueries = new WalletQueries(fixture.CreateConnectionFactory());
        var statementQueries = new StatementQueries(fixture.CreateConnectionFactory());
        var auditQueries = new AuditQueries(fixture.CreateConnectionFactory());
        var ledgerTransactionRepository = new LedgerTransactionRepository(context);
        var auditLogRepository = new AuditLogRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var idempotencyRepository = new IdempotencyRepository(context);
        var dailyUsageRepository = new DailyUsageRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var callerContext = new FakeCallerContext(callerCustomerId);
        var dailyLimitOptions = Options.Create(new DailyOutboundLimitOptions { LimitMinor = dailyLimitMinor });

        var walletService = new WalletService(walletRepository, walletQueries, statementQueries, auditQueries, ledgerTransactionRepository, auditLogRepository, outboxRepository, unitOfWork, callerContext);
        var transferService = new TransferService(
            walletRepository, ledgerTransactionRepository, auditLogRepository, outboxRepository,
            idempotencyRepository, dailyUsageRepository, unitOfWork, callerContext,
            timeProvider ?? TimeProvider.System, dailyLimitOptions);

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

    [Fact]
    public async Task TransferAsync_At_Exactly_The_Daily_Limit_Succeeds()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(1_000_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, customerId, dailyLimitMinor: 500_000);

        var result = await transfers.TransferAsync(
            Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 500_000 },
            CancellationToken.None);

        Assert.Equal(500_000, result.NewSourceBalanceMinor);
    }

    [Fact]
    public async Task TransferAsync_One_Kobo_Over_Daily_Limit_Is_Rejected_And_Balance_Unchanged()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(1_000_000);

        await using var context = fixture.CreateDbContext();
        var (_, transfers) = CreateServices(context, customerId, dailyLimitMinor: 500_000);

        var exception = await Assert.ThrowsAsync<DailyLimitExceededException>(() =>
            transfers.TransferAsync(
                Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 500_001 },
                CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);

        await using var verifyContext = fixture.CreateDbContext();
        var source = await verifyContext.Wallets.FindAsync(sourceId);
        Assert.Equal(1_000_000, source!.BalanceMinor); // untouched — rejected before mutation
    }

    [Fact]
    public async Task TransferAsync_Accumulates_Usage_Across_Multiple_Transfers_Same_Day()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(1_000_000);

        await using var context1 = fixture.CreateDbContext();
        var (_, transfers1) = CreateServices(context1, customerId, dailyLimitMinor: 500_000);
        await transfers1.TransferAsync(Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 300_000 },
            CancellationToken.None);

        // A second transfer that alone is within the limit, but combined with the first exceeds it.
        await using var context2 = fixture.CreateDbContext();
        var (_, transfers2) = CreateServices(context2, customerId, dailyLimitMinor: 500_000);

        var exception = await Assert.ThrowsAsync<DailyLimitExceededException>(() =>
            transfers2.TransferAsync(Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 300_000 },
                CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task CreditAsync_Does_Not_Count_Against_Daily_Outbound_Limit()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(500_000);

        await using var context = fixture.CreateDbContext();
        var (wallets, transfers) = CreateServices(context, customerId, dailyLimitMinor: 500_000);

        // Credit well beyond the daily limit — this must not be constrained by it at all.
        await wallets.CreditAsync(sourceId, new CreditWalletRequest { AmountMinor = 2_000_000 }, CancellationToken.None);

        // Outbound transfer up to the full daily limit should still succeed.
        var result = await transfers.TransferAsync(
            Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 500_000 },
            CancellationToken.None);

        Assert.Equal(2_000_000, result.NewSourceBalanceMinor); // 500,000 + 2,000,000 - 500,000
    }

    [Fact]
    public async Task TransferAsync_Usage_Resets_After_Wat_Midnight()
    {
        var (sourceId, destinationId, customerId) = await CreateFundedPairAsync(1_000_000);

        // 23:30 UTC on day 1 is 00:30 WAT on day 2 (WAT = UTC+1) — start there so the first
        // transfer lands on WAT-day-2, then cross into WAT-day-3.
        var day2Wat = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero));

        await using var context1 = fixture.CreateDbContext();
        var (_, transfers1) = CreateServices(context1, customerId, day2Wat, dailyLimitMinor: 500_000);
        await transfers1.TransferAsync(Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 500_000 },
            CancellationToken.None);

        // Still WAT-day-2 — the limit for that day is now exhausted.
        await using var context2 = fixture.CreateDbContext();
        var (_, transfers2) = CreateServices(context2, customerId, day2Wat, dailyLimitMinor: 500_000);
        await Assert.ThrowsAsync<DailyLimitExceededException>(() =>
            transfers2.TransferAsync(Guid.NewGuid().ToString(),
                new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 1 },
                CancellationToken.None));

        // Advance past WAT midnight into WAT-day-3 — the limit should have reset.
        var day3Wat = new ManualTimeProvider(new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.Zero));
        await using var context3 = fixture.CreateDbContext();
        var (_, transfers3) = CreateServices(context3, customerId, day3Wat, dailyLimitMinor: 500_000);

        var result = await transfers3.TransferAsync(Guid.NewGuid().ToString(),
            new TransferRequest { SourceWalletId = sourceId, DestinationWalletId = destinationId, AmountMinor = 500_000 },
            CancellationToken.None);

        Assert.Equal(0, result.NewSourceBalanceMinor);
    }
}
