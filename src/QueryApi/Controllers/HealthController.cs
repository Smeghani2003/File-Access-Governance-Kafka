using FileAccessGovernance.QueryApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileAccessGovernance.QueryApi.Controllers;

/// <summary>
/// Split into /live and /ready per the design doc §4 fix: a single endpoint that
/// checks SQL Server for BOTH liveness and readiness means a transient database
/// blip makes Kubernetes restart a perfectly healthy process (liveness failure),
/// instead of just pulling it out of the load-balancing rotation (readiness failure).
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly FileAccessGovernanceDbContext _db;

    public HealthController(FileAccessGovernanceDbContext db) => _db = db;

    /// <summary>No dependency checks — point the Kubernetes liveness probe here.</summary>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "ok" });

    /// <summary>Checks SQL Server connectivity — point the Kubernetes readiness probe here.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return Ok(new { status = "ok" });
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable" });
        }
    }
}
