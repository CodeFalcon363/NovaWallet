using NovaWallet.Core.Interfaces;

namespace NovaWallet.Test.TestSupport;

public class FakeCallerContext(string actorId, string ipAddress = "127.0.0.1") : ICallerContext
{
    public string ActorId { get; } = actorId;
    public string IpAddress { get; } = ipAddress;
    public string CorrelationId { get; } = Guid.NewGuid().ToString();
}
