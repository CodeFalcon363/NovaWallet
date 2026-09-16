using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class IdempotencyRepository(NovaWalletDbContext context) : IIdempotencyRepository
{
    /// <summary>
    /// Deliberately AsNoTracking + a direct query, not FindAsync: this is polled in a loop by
    /// TransferService to observe a status change made by a DIFFERENT request. FindAsync would
    /// return the same cached tracked instance from this context's identity map on every call
    /// after the first, so the poller would never see the row transition out of Pending.
    /// </summary>
    public Task<TransferIdempotencyRecord?> GetAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        context.TransferIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<bool> TryReserveAsync(string idempotencyKey, string requestFingerprint, CancellationToken cancellationToken)
    {
        var record = new TransferIdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            Status = IdempotencyStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.TransferIdempotencyRecords.Add(record);

        try
        {
            // Deliberately saved immediately (not batched with the caller's later SaveChanges):
            // the primary-key insert is what serializes concurrent requests replaying the same
            // key, and that guarantee only holds once this row is actually committed.
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (SqlExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            context.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkCompletedAsync(string idempotencyKey, Guid resultTransactionId, CancellationToken cancellationToken)
    {
        var record = await context.TransferIdempotencyRecords.FindAsync([idempotencyKey], cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency record '{idempotencyKey}' was not reserved before completion.");

        record.Status = IdempotencyStatus.Completed;
        record.ResultTransactionId = resultTransactionId;
        record.CompletedAtUtc = DateTime.UtcNow;
    }

    public async Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var record = await context.TransferIdempotencyRecords.FindAsync([idempotencyKey], cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency record '{idempotencyKey}' was not reserved before failure.");

        record.Status = IdempotencyStatus.Failed;
        record.CompletedAtUtc = DateTime.UtcNow;
    }
}
