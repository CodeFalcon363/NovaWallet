using NovaWallet.Core.Interfaces;

namespace NovaWallet.Test.TestSupport;

public class FakeEventPublisher : IEventPublisher
{
    public List<(string EventType, string PayloadJson, string? CorrelationId)> Published { get; } = [];

    /// <summary>When set, every PublishAsync call throws — simulates a broker outage.</summary>
    public bool ShouldFail { get; set; }

    public Task PublishAsync(string eventType, string payloadJson, string? correlationId, CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Simulated broker failure.");
        }

        Published.Add((eventType, payloadJson, correlationId));
        return Task.CompletedTask;
    }
}
