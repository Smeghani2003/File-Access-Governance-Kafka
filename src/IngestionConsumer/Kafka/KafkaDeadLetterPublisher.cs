using Confluent.Kafka;
using FileAccessGovernance.Shared.Kafka;
using Microsoft.Extensions.Options;

namespace FileAccessGovernance.IngestionConsumer.Kafka;

public sealed class KafkaDeadLetterPublisher : IDeadLetterPublisher, IAsyncDisposable
{
    private readonly IProducer<Null, string> _producer;

    public KafkaDeadLetterPublisher(IOptions<KafkaConsumerOptions> options)
    {
        var config = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _producer = new ProducerBuilder<Null, string>(config).Build();
    }

    public async Task PublishAsync(string rawMessage, string reason, CancellationToken ct)
    {
        var headers = new Headers { { "x-dlq-reason", System.Text.Encoding.UTF8.GetBytes(reason) } };
        await _producer.ProduceAsync(
            KafkaTopics.ObjectsRawDeadLetter,
            new Message<Null, string> { Value = rawMessage, Headers = headers },
            ct);
    }

    public ValueTask DisposeAsync()
    {
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
