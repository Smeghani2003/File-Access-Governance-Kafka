namespace FileAccessGovernance.Shared.Models;

/// <summary>
/// The Kafka message shape published to fs.objects.raw by the Scan Agent and
/// consumed by the Ingestion Consumer. Carries a path, not an ID — ObjectId is a
/// SQL Server IDENTITY value the agent has no way to know. See design doc §5.1.1.
/// </summary>
public sealed record ObjectRecord(
    string FullPath,
    string? ParentPath,          // null only for the share root
    bool IsDirectory,
    string DescriptorHash,
    bool IsInheritanceBreak,
    string ShareName,
    DateTime ScannedUtc,
    SecurityDescriptorRecord? NewDescriptor); // null when this hash was already reported by this agent
