namespace FileAccessGovernance.ScanAgent.WorkQueue;

/// <summary>
/// Work-stealing queue for a single agent process scanning one file system — see
/// the earlier work-stealing discussion and design doc §9 item 8. This is an
/// in-process queue (System.Threading.Channels), which is sufficient for Phase 1's
/// scope of one agent per share. Coordinating a work-stealing queue *across*
/// multiple agent processes on the same tree (the distributed version) would need
/// a shared coordination layer (e.g. a Kafka task topic or Redis) and is out of
/// scope here — nothing downstream (Kafka message shape, SQL schema) would need to
/// change to add it later.
/// </summary>
public interface IDirectoryTaskQueue
{
    void Enqueue(DirectoryTask task);

    /// <summary>Signals that one previously-dequeued task has finished processing
    /// (including any new subdirectories it discovered having been enqueued) —
    /// the queue completes itself once there's no work in flight anywhere.</summary>
    void MarkComplete();

    IAsyncEnumerable<DirectoryTask> DequeueAllAsync(CancellationToken ct);
}
