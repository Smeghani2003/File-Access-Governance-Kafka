using FileAccessGovernance.QueryApi.Dtos;
using FileAccessGovernance.Shared;

namespace FileAccessGovernance.QueryApi.Services;

public interface IFolderAccessService
{
    Task<FolderAccessResponse?> GetAccessAsync(string path, CancellationToken ct);
}

/// <summary>Implements design doc §5.2. Depends on IFsObjectRepository, not a raw
/// DbContext, so it can be unit-tested with a mock — see §8.</summary>
public sealed class FolderAccessService : IFolderAccessService
{
    private readonly IFsObjectRepository _repository;
    private readonly ISidNameResolver _sidResolver;

    public FolderAccessService(IFsObjectRepository repository, ISidNameResolver sidResolver)
    {
        _repository = repository;
        _sidResolver = sidResolver;
    }

    public async Task<FolderAccessResponse?> GetAccessAsync(string path, CancellationToken ct)
    {
        var normalized = PathNormalizer.Normalize(path);
        var pathHash = HashUtil.Sha256Bytes(normalized);

        var obj = await _repository.FindByPathHashAsync(pathHash, ct);
        if (obj is null) return null; // controller maps this to 404

        var aces = await _repository.GetAcesForDescriptorAsync(obj.DescriptorId, ct);
        var names = await _sidResolver.ResolveNamesAsync(aces, ct);

        return FolderAccessResponseMapper.ToResponse(obj, aces, names);
    }
}
