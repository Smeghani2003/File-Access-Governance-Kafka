using FileAccessGovernance.QueryApi.Data;

namespace FileAccessGovernance.QueryApi.Services;

public interface ISidNameResolver
{
    /// <summary>Returns a Sid -> DisplayName map; value is null for an unresolved/orphaned SID.</summary>
    Task<IReadOnlyDictionary<string, string?>> ResolveNamesAsync(IReadOnlyList<SecurityDescriptorAce> aces, CancellationToken ct);
}
