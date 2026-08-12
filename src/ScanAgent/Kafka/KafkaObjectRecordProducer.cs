using System.Text.Json;
using Confluent.Kafka;
using FileAccessGovernance.Shared;
using FileAccessGovernance.Shared.Kafka;
using FileAccessGovernance.Shared.Models;
using Microsoft.Extensions.Options;

namespace FileAccessGovernance.ScanAgent.Kafka;

public sealed class ScanAgentKafkaOptions
{
    public string BootstrapServers { get; set; } = default!;
}

/// <summary>
/// Partition key = hash(path), per design doc §5.1.1 — guarantees every update for
/// a given object always lands on the same partition in order, even with multiple
/// agents publishing concurrently. This is also the reason ParentObjectId can't be
/// resolved inline (see MergeRunner/usp_MergeFsObjectsBatch): a parent and its
/// child hash to different keys and can land on different partitions.
/// </summary>
public sealed class KafkaObjectRecordProducer : IObjectRecordProducer
{
    private readonly IProducer<string, string> _producer;

    public KafkaObjectRecordProducer(IOptions<ScanAgentKafkaOptions> options)
    {
        var config = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(ObjectRecord record, CancellationToken ct)
    {
        var normalizedPath = PathNormalizer.Normalize(record.FullPath);
        var key = Convert.ToHexString(HashUtil.Sha256Bytes(normalizedPath));
        var value = JsonSerializer.Serialize(record);

        await _producer.ProduceAsync(KafkaTopics.ObjectsRaw, new Message<string, string> { Key = key, Value = value }, ct);
    }

    public void Dispose() => _producer.Dispose();
}
