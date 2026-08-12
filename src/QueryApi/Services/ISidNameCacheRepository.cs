using FileAccessGovernance.QueryApi.Data;

namespace FileAccessGovernance.QueryApi.Services;

public interface ISidNameCacheRepository
{
    Task<SidNameCacheEntry?> GetAsync(string sid, CancellationToken ct);
    Task UpsertAsync(string sid, string? displayName, DateTime resolvedUtc, CancellationToken ct);
}
