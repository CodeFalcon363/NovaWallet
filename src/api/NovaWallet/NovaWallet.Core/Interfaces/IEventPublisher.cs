namespace NovaWallet.Core.Interfaces;

/// <summary>Message-queue publishing boundary — implemented in Infrastructure (RabbitMQ).</summary>
public interface IEventPublisher
{
    Task PublishAsync(string eventType, string payloadJson, string? correlationId, CancellationToken cancellationToken);
}
