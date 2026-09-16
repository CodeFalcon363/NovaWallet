using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class OutboxRepository(NovaWalletDbContext context) : IOutboxRepository
{
    public void Add(OutboxMessage message) => context.OutboxMessages.Add(message);

    public Task<List<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken) =>
        context.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task MarkProcessedAsync(Guid outboxMessageId, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.FindAsync([outboxMessageId], cancellationToken)
            ?? throw new InvalidOperationException($"Outbox message '{outboxMessageId}' was not found.");

        message.ProcessedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task IncrementAttemptsAsync(Guid outboxMessageId, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.FindAsync([outboxMessageId], cancellationToken)
            ?? throw new InvalidOperationException($"Outbox message '{outboxMessageId}' was not found.");

        message.Attempts += 1;
        await context.SaveChangesAsync(cancellationToken);
    }
}
