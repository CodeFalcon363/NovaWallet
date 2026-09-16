using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class IdempotencyRepository(NovaWalletDbContext context) : IIdempotencyRepository
{
    private const int SqlUniqueConstraintViolation = 2627;
    private const int SqlDuplicateKeyViolation = 2601;

    public Task<TransferIdempotencyRecord?> GetAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        context.TransferIdempotencyRecords.FindAsync([idempotencyKey], cancellationToken).AsTask();

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
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == SqlUniqueConstraintViolation || sqlEx.Number == SqlDuplicateKeyViolation);
}
