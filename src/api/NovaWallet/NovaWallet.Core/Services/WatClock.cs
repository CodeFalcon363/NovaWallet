namespace NovaWallet.Core.Services;

/// <summary>
/// West Africa Time is a fixed UTC+1 offset — Nigeria does not observe DST — so "today in WAT"
/// is derived directly, no timezone database lookup needed (BRD Assumption 3).
/// </summary>
public static class WatClock
{
    private static readonly TimeSpan WatOffset = TimeSpan.FromHours(1);

    public static DateOnly TodayWat(TimeProvider timeProvider) =>
        DateOnly.FromDateTime((timeProvider.GetUtcNow() + WatOffset).DateTime);
}
