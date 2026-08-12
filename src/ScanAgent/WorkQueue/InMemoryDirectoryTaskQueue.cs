using System.Threading.Channels;

namespace FileAccessGovernance.ScanAgent.WorkQueue;

public sealed class InMemoryDirectoryTaskQueue : IDirectoryTaskQueue
{
    private readonly Channel<DirectoryTask> _channel = Channel.CreateUnbounded<DirectoryTask>();

    // Tracks work in flight (enqueued-but-not-yet-fully-processed tasks) so the
    // queue can close itself once a scan is genuinely finished, rather than a
    // worker having to guess "is the tree done, or did I just get unlucky and
    // catch it empty for a moment while a sibling worker is about to enqueue more".
    private int _pendingCount;

    public void Enqueue(DirectoryTask task)
    {
        Interlocked.Increment(ref _pendingCount);
        if (!_channel.Writer.TryWrite(task))
        {
            throw new InvalidOperationException("Failed to enqueue directory task — channel writer closed unexpectedly.");
        }
    }

    public void MarkComplete()
    {
        if (Interlocked.Decrement(ref _pendingCount) == 0)
        {
            _channel.Writer.TryComplete();
        }
    }

    public IAsyncEnumerable<DirectoryTask> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
