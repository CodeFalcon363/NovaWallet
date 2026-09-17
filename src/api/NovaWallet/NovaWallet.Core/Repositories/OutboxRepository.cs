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
}
