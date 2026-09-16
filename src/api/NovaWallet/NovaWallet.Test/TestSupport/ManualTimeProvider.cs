namespace NovaWallet.Test.TestSupport;

/// <summary>Minimal settable TimeProvider double — lets tests move "now" without waiting for real time.</summary>
public class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset now) => _now = now;
}
