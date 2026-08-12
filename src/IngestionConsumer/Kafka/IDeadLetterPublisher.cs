namespace FileAccessGovernance.IngestionConsumer.Kafka;

/// <summary>Design doc §6 "Poison messages" — a message that can never be processed
/// (e.g. fails to deserialize) goes here instead of blocking its partition forever.</summary>
public interface IDeadLetterPublisher
{
    Task PublishAsync(string rawMessage, string reason, CancellationToken ct);
}
