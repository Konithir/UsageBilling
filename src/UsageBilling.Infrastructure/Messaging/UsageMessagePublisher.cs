using Confluent.Kafka;
using Microsoft.Extensions.Options;
using UsageBilling.Domain;
using UsageBilling.Domain.Models;
using UsageBilling.Infrastructure.Configuration;

namespace UsageBilling.Infrastructure.Messaging;

public sealed class UsageMessagePublisher : IUsageMessagePublisher, IDisposable
{
    private readonly MessagingOptions options;
    private readonly IProducer<string, string>? producer;

    public UsageMessagePublisher(IOptions<MessagingOptions> options)
    {
        this.options = options.Value;
        if (!string.Equals(this.options.Provider, "Demo", StringComparison.OrdinalIgnoreCase))
        {
            var config = new ProducerConfig
            {
                BootstrapServers = this.options.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All,
                ClientId = "usage-billing-api"
            };
            if (!string.IsNullOrWhiteSpace(this.options.SaslUsername))
            {
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = this.options.SaslUsername;
                config.SaslPassword = this.options.SaslPassword;
            }
            producer = new ProducerBuilder<string, string>(config).Build();
        }
    }

    public string Mode => options.Provider;
    public string Endpoint => string.Equals(options.Provider, "Demo", StringComparison.OrdinalIgnoreCase) ? "local outbox" : $"{options.BootstrapServers}/{options.Topic}";

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (producer is null) return;
        await producer.ProduceAsync(options.Topic, new Message<string, string>
        {
            Key = message.AggregateId,
            Value = message.Payload,
            Headers = new Headers { new("event-type", System.Text.Encoding.UTF8.GetBytes(message.Type)) }
        }, cancellationToken);
    }

    public void Dispose() => producer?.Dispose();
}