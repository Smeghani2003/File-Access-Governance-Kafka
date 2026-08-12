namespace FileAccessGovernance.IngestionConsumer.Kafka;

public sealed class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = default!;
    public string GroupId { get; set; } = "ingestion-consumer";
    public int MaxBatchSize { get; set; } = 5000;
    public int BatchWindowMilliseconds { get; set; } = 2000;
    public int MaxDeliveryAttempts { get; set; } = 3;
}
