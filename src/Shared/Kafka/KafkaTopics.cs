namespace FileAccessGovernance.Shared.Kafka;

public static class KafkaTopics
{
    /// <summary>Partition key = hash(path) — see design doc §5.1.1 for why, and the
    /// tradeoff it accepts (parent/child ordering isn't guaranteed across partitions).</summary>
    public const string ObjectsRaw = "fs.objects.raw";

    /// <summary>Destination for a message that failed processing repeatedly — see design doc §6.</summary>
    public const string ObjectsRawDeadLetter = "fs.objects.raw.dlq";
}
