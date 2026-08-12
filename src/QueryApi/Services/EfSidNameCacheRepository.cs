using FileAccessGovernance.QueryApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FileAccessGovernance.QueryApi.Services;

public sealed class EfSidNameCacheRepository : ISidNameCacheRepository
{
    private readonly FileAccessGovernanceDbContext _db;

    public EfSidNameCacheRepository(FileAccessGovernanceDbContext db) => _db = db;

    public Task<SidNameCacheEntry?> GetAsync(string sid, CancellationToken ct) =>
        _db.SidNameCache.AsNoTracking().SingleOrDefaultAsync(x => x.Sid == sid, ct);

    public async Task UpsertAsync(string sid, string? displayName, DateTime resolvedUtc, CancellationToken ct)
    {
        var existing = await _db.SidNameCache.SingleOrDefaultAsync(x => x.Sid == sid, ct);
        if (existing is null)
        {
            _db.SidNameCache.Add(new SidNameCacheEntry { Sid = sid, DisplayName = displayName, ResolvedUtc = resolvedUtc });
        }
        else
        {
            existing.DisplayName = displayName;
            existing.ResolvedUtc = resolvedUtc;
        }
        await _db.SaveChangesAsync(ct);
    }
}
