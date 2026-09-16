using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Repositories;
using NovaWallet.Core.Services;
using NovaWallet.Test.TestSupport;
using Xunit;

namespace NovaWallet.Test.Services;

[Collection(SqlServerCollection.Name)]
public class OutboxDispatcherServiceTests(SqlServerFixture fixture)
{
    // The shared test database accumulates unprocessed outbox rows from every other test in
    // this collection (nothing else drains them). DispatchPendingAsync only processes a bounded
    // batch per call, so a single call is not guaranteed to reach a specific just-added message —
    // this drains repeatedly until it does, bounded so a genuine bug still fails the test.
    private static async Task DispatchUntilAsync(OutboxDispatcherService dispatcher, Func<Task<bool>> isDone)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await isDone())
            {
                return;
            }

            if (await dispatcher.DispatchPendingAsync(CancellationToken.None) == 0)
            {
                break; // nothing left to drain this round; re-check isDone once more below
            }
        }

        Assert.True(await isDone(), "Outbox backlog did not drain to the expected state in time.");
    }

    [Fact]
    public async Task DispatchPendingAsync_Publishes_And_Marks_Processed()
    {
        await using var context = fixture.CreateDbContext();
        var message = new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = "WalletCreated",
            PayloadJson = """{"walletId":"11111111-1111-1111-1111-111111111111"}""",
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var dispatcher = new OutboxDispatcherService(new OutboxRepository(context), publisher);

        await DispatchUntilAsync(dispatcher, async () =>
        {
            await using var checkContext = fixture.CreateDbContext();
            var current = await checkContext.OutboxMessages.FindAsync(message.OutboxMessageId);
            return current!.ProcessedAtUtc is not null;
        });

        Assert.Contains(publisher.Published, p => p.EventType == "WalletCreated" && p.PayloadJson == message.PayloadJson);
    }

    [Fact]
    public async Task DispatchPendingAsync_On_Publish_Failure_Leaves_Unprocessed_And_Increments_Attempts()
    {
        await using var context = fixture.CreateDbContext();
        var message = new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = "WalletCredited",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var publisher = new FakeEventPublisher { ShouldFail = true };
        var dispatcher = new OutboxDispatcherService(new OutboxRepository(context), publisher);

        await DispatchUntilAsync(dispatcher, async () =>
        {
            await using var checkContext = fixture.CreateDbContext();
            var current = await checkContext.OutboxMessages.FindAsync(message.OutboxMessageId);
            return current!.Attempts > 0;
        });

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.OutboxMessages.FindAsync(message.OutboxMessageId);
        Assert.Null(persisted!.ProcessedAtUtc);
        Assert.True(persisted.Attempts >= 1);
    }

    [Fact]
    public async Task DispatchPendingAsync_Does_Not_Republish_Already_Processed_Messages()
    {
        await using var context = fixture.CreateDbContext();
        var alreadyProcessed = new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = "TransferCompleted",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ProcessedAtUtc = DateTime.UtcNow.AddMinutes(-4),
        };
        context.OutboxMessages.Add(alreadyProcessed);
        await context.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var dispatcher = new OutboxDispatcherService(new OutboxRepository(context), publisher);

        // Drain whatever backlog exists (from other tests) so this isn't vacuously true just
        // because the batch never got that far.
        while (await dispatcher.DispatchPendingAsync(CancellationToken.None) > 0)
        {
        }

        Assert.DoesNotContain(publisher.Published, p => p.PayloadJson == alreadyProcessed.PayloadJson && p.EventType == "TransferCompleted");
    }
}
