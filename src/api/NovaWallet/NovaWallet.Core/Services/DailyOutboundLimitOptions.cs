namespace NovaWallet.Core.Services;

public class DailyOutboundLimitOptions
{
    public const string SectionName = "DailyOutboundLimit";

    public long LimitMinor { get; set; }
}
