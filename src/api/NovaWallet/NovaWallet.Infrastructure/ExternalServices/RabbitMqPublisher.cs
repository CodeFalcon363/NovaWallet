using System.Text;
using Microsoft.Extensions.Options;
using NovaWallet.Core.Interfaces;
using RabbitMQ.Client;

namespace NovaWallet.Infrastructure.ExternalServices;

/// <summary>
/// Publishes outbox events to RabbitMQ (BRD §9/NFR-ARCH-5). A single long-lived connection is
/// reused; a fresh channel is opened per publish since IModel is not safe for concurrent use
/// and the dispatcher only calls this from one background loop at a time.
/// </summary>
public class RabbitMqPublisher : IEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly Lazy<IConnection> _connection;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
        _connection = new Lazy<IConnection>(CreateConnection);
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
        };
        return factory.CreateConnection("novawallet-outbox-dispatcher");
    }

    public Task PublishAsync(string eventType, string payloadJson, string? correlationId, CancellationToken cancellationToken)
    {
        using var channel = _connection.Value.CreateModel();
        channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.CorrelationId = correlationId;

        var body = Encoding.UTF8.GetBytes(payloadJson);
        channel.BasicPublish(_options.ExchangeName, routingKey: eventType, properties, body);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
