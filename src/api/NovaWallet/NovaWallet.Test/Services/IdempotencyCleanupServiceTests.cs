using NovaWallet.Core.Entities;
using NovaWallet.Core.Repositories;
using NovaWallet.Core.Services;
using NovaWallet.Test.TestSupport;
using Xunit;

namespace NovaWallet.Test.Services;

[Collection(SqlServerCollection.Name)]
public class IdempotencyCleanupServiceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task CleanupExpiredAsync_Deletes_Expired_Completed_And_Failed_Records()
    {
        var now = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var expiredCompletedKey = $"key-{Guid.NewGuid():N}";
        var expiredFailedKey = $"key-{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        context.TransferIdempotencyRecords.AddRange(
            new TransferIdempotencyRecord
            {
                IdempotencyKey = expiredCompletedKey,
                RequestFingerprint = "fp",
                Status = IdempotencyStatus.Completed,
                CreatedAtUtc = now.GetUtcNow().UtcDateTime.AddDays(-2),
                ExpiresAtUtc = now.GetUtcNow().UtcDateTime.AddHours(-1), // already expired
            },
            new TransferIdempotencyRecord
            {
                IdempotencyKey = expiredFailedKey,
                RequestFingerprint = "fp",
                Status = IdempotencyStatus.Failed,
                CreatedAtUtc = now.GetUtcNow().UtcDateTime.AddDays(-2),
                ExpiresAtUtc = now.GetUtcNow().UtcDateTime.AddHours(-1),
            });
        await context.SaveChangesAsync();

        var cleanup = new IdempotencyCleanupService(new IdempotencyRepository(context), now);
        await cleanup.CleanupExpiredAsync(CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Null(await verifyContext.TransferIdempotencyRecords.FindAsync(expiredCompletedKey));
        Assert.Null(await verifyContext.TransferIdempotencyRecords.FindAsync(expiredFailedKey));
    }

    [Fact]
    public async Task CleanupExpiredAsync_Never_Deletes_Pending_Records_Even_If_Past_Expiry()
    {
        var now = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var stuckPendingKey = $"key-{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        context.TransferIdempotencyRecords.Add(new TransferIdempotencyRecord
        {
            IdempotencyKey = stuckPendingKey,
            RequestFingerprint = "fp",
            Status = IdempotencyStatus.Pending,
            CreatedAtUtc = now.GetUtcNow().UtcDateTime.AddDays(-2),
            ExpiresAtUtc = now.GetUtcNow().UtcDateTime.AddHours(-1), // "expired" but still Pending
        });
        await context.SaveChangesAsync();

        var cleanup = new IdempotencyCleanupService(new IdempotencyRepository(context), now);
        await cleanup.CleanupExpiredAsync(CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.NotNull(await verifyContext.TransferIdempotencyRecords.FindAsync(stuckPendingKey));
    }

    [Fact]
    public async Task CleanupExpiredAsync_Does_Not_Delete_Records_Not_Yet_Expired()
    {
        var now = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var liveKey = $"key-{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        context.TransferIdempotencyRecords.Add(new TransferIdempotencyRecord
        {
            IdempotencyKey = liveKey,
            RequestFingerprint = "fp",
            Status = IdempotencyStatus.Completed,
            CreatedAtUtc = now.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = now.GetUtcNow().UtcDateTime.AddHours(23), // not expired yet
        });
        await context.SaveChangesAsync();

        var cleanup = new IdempotencyCleanupService(new IdempotencyRepository(context), now);
        await cleanup.CleanupExpiredAsync(CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.NotNull(await verifyContext.TransferIdempotencyRecords.FindAsync(liveKey));
    }
}
