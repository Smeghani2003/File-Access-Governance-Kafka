using FileAccessGovernance.QueryApi.Data;

namespace FileAccessGovernance.QueryApi.Services;

/// <summary>
/// Minimal MVP version described in design doc §5.3: check the cache first, and on a
/// miss (or a stale entry), do a single LDAP lookup and write the result back —
/// including a cached "not found" so repeated lookups for an orphaned SID don't hit
/// AD every time. Does NOT expand nested group membership; see design doc §9.
/// </summary>
public sealed class SidNameResolver : ISidNameResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly ISidNameCacheRepository _cache;
    private readonly ISidDirectoryLookup _directory;
    private readonly TimeProvider _clock;

    public SidNameResolver(ISidNameCacheRepository cache, ISidDirectoryLookup directory, TimeProvider clock)
    {
        _cache = cache;
        _directory = directory;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ResolveNamesAsync(
        IReadOnlyList<SecurityDescriptorAce> aces, CancellationToken ct)
    {
        var result = new Dictionary<string, string?>();

        foreach (var sid in aces.Select(a => a.TrusteeSid).Distinct())
        {
            result[sid] = await ResolveOneAsync(sid, ct);
        }

        return result;
    }

    private async Task<string?> ResolveOneAsync(string sid, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(sid, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (cached is not null && now - cached.ResolvedUtc < CacheTtl)
        {
            return cached.DisplayName;
        }

        var displayName = await _directory.LookupDisplayNameAsync(sid, ct);
        await _cache.UpsertAsync(sid, displayName, now, ct);
        return displayName;
    }
}
