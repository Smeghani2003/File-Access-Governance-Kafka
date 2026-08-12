using FileAccessGovernance.QueryApi.Dtos;
using FileAccessGovernance.QueryApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileAccessGovernance.QueryApi.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AccessController : ControllerBase
{
    private readonly IFolderAccessService _accessService;
    private readonly IFsObjectRepository _repository;

    public AccessController(IFolderAccessService accessService, IFsObjectRepository repository)
    {
        _accessService = accessService;
        _repository = repository;
    }

    /// <summary>Design doc §4 — GET /api/v1/access/folder?path=...</summary>
    [HttpGet("access/folder")]
    [ProducesResponseType(typeof(FolderAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorEnvelope), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolderAccess([FromQuery] string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new ErrorEnvelope(new ErrorDetail("PATH_REQUIRED", "The 'path' query parameter is required.")));
        }

        var response = await _accessService.GetAccessAsync(path, ct);
        if (response is null)
        {
            return NotFound(new ErrorEnvelope(new ErrorDetail("PATH_NOT_FOUND", "No scanned object matches this path.")));
        }

        return Ok(response);
    }

    /// <summary>Design doc §4 — GET /api/v1/objects/{objectId}</summary>
    [HttpGet("objects/{objectId:long}")]
    [ProducesResponseType(typeof(ObjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorEnvelope), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetObject(long objectId, CancellationToken ct)
    {
        var obj = await _repository.FindByIdAsync(objectId, ct);
        if (obj is null)
        {
            return NotFound(new ErrorEnvelope(new ErrorDetail("OBJECT_NOT_FOUND", "No object exists with this id.")));
        }

        return Ok(new ObjectDto(obj.ObjectId, obj.FullPath, obj.ParentObjectId, obj.IsDirectory, obj.DescriptorId, obj.LastScannedUtc));
    }
}
