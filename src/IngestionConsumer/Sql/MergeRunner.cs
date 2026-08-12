using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace FileAccessGovernance.IngestionConsumer.Sql;

/// <summary>
/// Calls usp_MergeFsObjectsBatch (/db/procedures/usp_MergeFsObjectsBatch.sql) on the
/// SAME connection StagingWriter just populated — the procedure references local
/// temp tables, which only exist on that connection's session.
///
/// Retries on SQL Server deadlock (error 1205) — a documented possibility any time
/// multiple sessions run MERGE/UPDATE against overlapping rows concurrently, even
/// with the WITH (HOLDLOCK) hint the procedure uses to prevent the more specific
/// duplicate-insert race. See design doc §6 (Polly for transient errors).
/// </summary>
public sealed class MergeRunner
{
    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqlException>(ex => ex.Number == 1205),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200)
        })
        .Build();

    public async Task RunMergeAsync(SqlConnection connection, CancellationToken ct)
    {
        await RetryPipeline.ExecuteAsync(async token =>
        {
            await using var command = new SqlCommand("dbo.usp_MergeFsObjectsBatch", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
                CommandTimeout = 120
            };
            await command.ExecuteNonQueryAsync(token);
        }, ct);
    }
}