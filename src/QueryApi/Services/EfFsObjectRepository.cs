using FileAccessGovernance.QueryApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FileAccessGovernance.QueryApi.Services;

public sealed class EfFsObjectRepository : IFsObjectRepository
{
    private readonly FileAccessGovernanceDbContext _db;

    public EfFsObjectRepository(FileAccessGovernanceDbContext db) => _db = db;

    public Task<FsObject?> FindByPathHashAsync(byte[] pathHash, CancellationToken ct) =>
        _db.FsObjects.AsNoTracking().SingleOrDefaultAsync(o => o.PathHash == pathHash, ct);

    public Task<FsObject?> FindByIdAsync(long objectId, CancellationToken ct) =>
        _db.FsObjects.AsNoTracking().SingleOrDefaultAsync(o => o.ObjectId == objectId, ct);

    public async Task<IReadOnlyList<SecurityDescriptorAce>> GetAcesForDescriptorAsync(long descriptorId, CancellationToken ct) =>
        await _db.SecurityDescriptorAces.AsNoTracking()
            .Where(a => a.DescriptorId == descriptorId)
            .ToListAsync(ct);
}
