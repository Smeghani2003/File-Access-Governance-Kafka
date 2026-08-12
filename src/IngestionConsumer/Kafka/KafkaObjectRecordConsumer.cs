using System.Text.Json;
using Confluent.Kafka;
using FileAccessGovernance.Shared.Kafka;
using FileAccessGovernance.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileAccessGovernance.IngestionConsumer.Kafka;

public sealed class KafkaObjectRecordConsumer : IObjectRecordConsumer
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IDeadLetterPublisher _deadLetterPublisher;
    private readonly KafkaConsumerOptions _options;
    private readonly ILogger<KafkaObjectRecordConsumer> _logger;

    public KafkaObjectRecordConsumer(
        IOptions<KafkaConsumerOptions> options,
        IDeadLetterPublisher deadLetterPublisher,
        ILogger<KafkaObjectRecordConsumer> logger)
    {
        _options = options.Value;
        _deadLetterPublisher = deadLetterPublisher;
        _logger = logger;

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false, // we commit manually, only after a batch is durably in SQL Server
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.ObjectsRaw);
    }

    public async IAsyncEnumerable<IReadOnlyList<ObjectRecord>> ConsumeBatchesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = new List<ObjectRecord>(_options.MaxBatchSize);
            var deadline = DateTime.UtcNow.AddMilliseconds(_options.BatchWindowMilliseconds);

            while (batch.Count < _options.MaxBatchSize && DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;

                ConsumeResult<Ignore, string>? result;
                try
                {
                    result = _consumer.Consume(remaining);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Kafka consume error, will retry on next poll");
                    continue;
                }

                if (result is null) continue; // timed out this poll, loop will re-check the deadline

                var record = TryDeserialize(result.Message.Value);
                if (record is null)
                {
                    // Permanent failure — a malformed message will never parse successfully,
                    // so there's no value in retrying it. Straight to the dead-letter topic
                    // rather than blocking this partition. See design doc §6.
                    await _deadLetterPublisher.PublishAsync(result.Message.Value, "deserialization failed", ct);
                }
                else
                {
                    batch.Add(record);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
    }

    public void CommitBatch() => _consumer.Commit();

    private ObjectRecord? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ObjectRecord>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize ObjectRecord from Kafka message");
            return null;
        }
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
    }
}
