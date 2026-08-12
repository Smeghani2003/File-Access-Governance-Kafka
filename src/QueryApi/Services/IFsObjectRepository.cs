using FileAccessGovernance.QueryApi.Data;

namespace FileAccessGovernance.QueryApi.Services;

/// <summary>
/// Wraps the EF Core DbContext behind an interface so FolderAccessService can be
/// unit-tested with a mock instead of needing a real database — see design doc §5.2/§8.
/// </summary>
public interface IFsObjectRepository
{
    Task<FsObject?> FindByPathHashAsync(byte[] pathHash, CancellationToken ct);
    Task<IReadOnlyList<SecurityDescriptorAce>> GetAcesForDescriptorAsync(long descriptorId, CancellationToken ct);
    Task<FsObject?> FindByIdAsync(long objectId, CancellationToken ct);
}
