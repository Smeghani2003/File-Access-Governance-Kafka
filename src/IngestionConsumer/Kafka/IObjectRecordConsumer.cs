using FileAccessGovernance.Shared.Models;

namespace FileAccessGovernance.IngestionConsumer.Kafka;

public interface IObjectRecordConsumer : IDisposable
{
    /// <summary>Yields a batch once MaxBatchSize is reached or BatchWindowMilliseconds
    /// elapses, whichever comes first — design doc §5.1 step 1.</summary>
    IAsyncEnumerable<IReadOnlyList<ObjectRecord>> ConsumeBatchesAsync(CancellationToken ct);

    /// <summary>Commits offsets for everything consumed so far. Call only after the
    /// batch has been durably written to SQL Server — design doc §5.1 step 6.</summary>
    void CommitBatch();
}
