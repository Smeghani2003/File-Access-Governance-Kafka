namespace FileAccessGovernance.QueryApi.Services;

/// <summary>
/// Abstraction over the actual Active Directory lookup, so local dev/tests don't
/// need a real domain controller — mirrors the ScanAgent's ISecurityDescriptorReader
/// fake pattern documented in design doc §7.
/// </summary>
public interface ISidDirectoryLookup
{
    /// <summary>Returns null if the SID doesn't resolve to any AD object (e.g. orphaned SID).</summary>
    Task<string?> LookupDisplayNameAsync(string sid, CancellationToken ct);
}

/// <summary>Always "unresolved" — the default for local dev without a real AD to query against.</summary>
public sealed class NullSidDirectoryLookup : ISidDirectoryLookup
{
    public Task<string?> LookupDisplayNameAsync(string sid, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
