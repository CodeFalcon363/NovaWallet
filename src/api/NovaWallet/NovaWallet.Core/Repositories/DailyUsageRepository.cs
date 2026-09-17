using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class DailyUsageRepository(NovaWalletDbContext context) : IDailyUsageRepository
{
    public async Task<DailyOutboundUsage> GetOrCreateTrackedAsync(Guid walletId, DateOnly usageDateWat, CancellationToken cancellationToken)
    {
        var existing = await context.DailyOutboundUsages.FindAsync([walletId, usageDateWat], cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new DailyOutboundUsage
        {
            WalletId = walletId,
            UsageDateWat = usageDateWat,
            OutboundTotalMinor = 0,
        };
        context.DailyOutboundUsages.Add(created);
        return created;
    }

    public Task ReloadAsync(DailyOutboundUsage usage, CancellationToken cancellationToken) =>
        context.Entry(usage).ReloadAsync(cancellationToken);
}
