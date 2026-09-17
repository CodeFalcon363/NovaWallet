using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Interfaces;

public interface IDailyUsageRepository
{
    /// <summary>Tracked read — creates and adds (not yet saved) a zeroed row if none exists yet.</summary>
    Task<DailyOutboundUsage> GetOrCreateTrackedAsync(Guid walletId, DateOnly usageDateWat, CancellationToken cancellationToken);

    Task ReloadAsync(DailyOutboundUsage usage, CancellationToken cancellationToken);
}
