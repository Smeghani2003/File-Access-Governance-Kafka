using FileAccessGovernance.IngestionConsumer.Kafka;
using FileAccessGovernance.IngestionConsumer.Sql;

namespace FileAccessGovernance.IngestionConsumer;

/// <summary>Design doc §5.1 — one SqlConnection per batch, write before commit,
/// so a crash mid-batch just re-processes the same batch (idempotent by hash).</summary>
public sealed class ConsumerWorker : BackgroundService
{
    private readonly IObjectRecordConsumer _consumer;
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly StagingWriter _stagingWriter;
    private readonly MergeRunner _mergeRunner;
    private readonly ILogger<ConsumerWorker> _logger;

    public ConsumerWorker(
        IObjectRecordConsumer consumer,
        ISqlConnectionFactory connectionFactory,
        StagingWriter stagingWriter,
        MergeRunner mergeRunner,
        ILogger<ConsumerWorker> logger)
    {
        _consumer = consumer;
        _connectionFactory = connectionFactory;
        _stagingWriter = stagingWriter;
        _mergeRunner = mergeRunner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in _consumer.ConsumeBatchesAsync(stoppingToken))
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["BatchSize"] = batch.Count });

            await using var connection = await _connectionFactory.OpenAsync(stoppingToken);
            await _stagingWriter.WriteBatchAsync(connection, batch, stoppingToken);
            await _mergeRunner.RunMergeAsync(connection, stoppingToken);

            _consumer.CommitBatch();
            _logger.LogInformation("Batch of {Count} objects committed", batch.Count);
        }
    }

    public override void Dispose()
    {
        _consumer.Dispose();
        base.Dispose();
    }
}
