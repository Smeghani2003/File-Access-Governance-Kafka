using System.Collections.Concurrent;
using FileAccessGovernance.ScanAgent.Kafka;
using FileAccessGovernance.ScanAgent.Security;
using FileAccessGovernance.ScanAgent.WorkQueue;
using FileAccessGovernance.Shared;
using FileAccessGovernance.Shared.Models;
using Microsoft.Extensions.Options;

namespace FileAccessGovernance.ScanAgent;

/// <summary>
/// Work-stealing traversal of one file share (see the earlier design discussion and
/// design doc §5.1.1). A directory's own ObjectRecord is emitted at the moment it's
/// *discovered* as a child of its parent — not when its own queued task is later
/// processed, since that task exists only to list what's *inside* it. The one
/// exception is the share root, which has no parent to be discovered from, so its
/// record is emitted once explicitly at startup.
/// </summary>
public sealed class ScanWorker : BackgroundService
{
    private readonly IDirectoryTaskQueue _queue;
    private readonly ISecurityDescriptorReader _descriptorReader;
    private readonly IObjectRecordProducer _producer;
    private readonly ScanOptions _options;
    private readonly ILogger<ScanWorker> _logger;

    // Tracks which descriptor hashes THIS agent has already reported in the current
    // run, so it only attaches the full SecurityDescriptorRecord (owner/SDDL/ACEs) the
    // first time — see design doc §5.1 step 2. A different agent, or this agent after
    // a restart, may harmlessly re-report the same descriptor; the merge dedupes it.
    private readonly ConcurrentDictionary<string, byte> _reportedDescriptorHashes = new();

    public ScanWorker(
        IDirectoryTaskQueue queue,
        ISecurityDescriptorReader descriptorReader,
        IObjectRecordProducer producer,
        IOptions<ScanOptions> options,
        ILogger<ScanWorker> logger)
    {
        _queue = queue;
        _descriptorReader = descriptorReader;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrivilegeEnabler.EnableBackupPrivilege();

        await PublishRootRecordAsync(stoppingToken);
        _queue.Enqueue(new DirectoryTask(_options.RootPath, ParentPath: null, _options.ShareName));

        var workers = Enumerable.Range(0, _options.DegreeOfParallelism)
            .Select(_ => ProcessQueueAsync(stoppingToken));
        await Task.WhenAll(workers);

        _logger.LogInformation("Scan of {RootPath} complete", _options.RootPath);
    }

    private async Task PublishRootRecordAsync(CancellationToken ct)
    {
        var info = _descriptorReader.Read(_options.RootPath, isDirectory: true);
        await PublishAsync(_options.RootPath, parentPath: null, isDirectory: true, info, ct);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var task in _queue.DequeueAllAsync(ct))
        {
            try
            {
                await ProcessDirectoryAsync(task, ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Even with SeBackupPrivilege, some objects can remain unreadable
                // (e.g. SACL-only restrictions) — log and move on rather than
                // aborting the whole scan. Per main plan §6 (production safety).
                _logger.LogWarning(ex, "Permission denied enumerating {Path}", task.FullPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error enumerating {Path}", task.FullPath);
            }
            finally
            {
                _queue.MarkComplete();
            }
        }
    }

    private async Task ProcessDirectoryAsync(DirectoryTask task, CancellationToken ct)
    {
        foreach (var entry in new DirectoryInfo(task.FullPath).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Don't follow symlinks/junctions — avoids the infinite-loop risk in
                // the main plan's risk list (§8). Offline/cloud-tiered file handling
                // (avoiding accidental rehydration) is flagged there too and is a
                // known gap not fully implemented in this Phase 1 MVP.
                continue;
            }

            var isDirectory = entry is DirectoryInfo;
            SecurityDescriptorInfo info;
            try
            {
                info = _descriptorReader.Read(entry.FullName, isDirectory);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Permission denied reading security descriptor for {Path}", entry.FullName);
                continue;
            }

            await PublishAsync(entry.FullName, task.FullPath, isDirectory, info, ct);

            if (isDirectory)
            {
                _queue.Enqueue(new DirectoryTask(entry.FullName, task.FullPath, task.ShareName));
            }
        }
    }

    private async Task PublishAsync(string fullPath, string? parentPath, bool isDirectory, SecurityDescriptorInfo info, CancellationToken ct)
    {
        var descriptorHash = HashUtil.Sha256Hex(info.RawSddl);
        var isNewDescriptor = _reportedDescriptorHashes.TryAdd(descriptorHash, 0);

        var record = new ObjectRecord(
            FullPath: fullPath,
            ParentPath: parentPath,
            IsDirectory: isDirectory,
            DescriptorHash: descriptorHash,
            IsInheritanceBreak: info.IsProtected,
            ShareName: _options.ShareName,
            ScannedUtc: DateTime.UtcNow,
            NewDescriptor: isNewDescriptor
                ? new SecurityDescriptorRecord(descriptorHash, info.OwnerSid, info.RawSddl, info.Aces)
                : null);

        await _producer.PublishAsync(record, ct);
    }
}
