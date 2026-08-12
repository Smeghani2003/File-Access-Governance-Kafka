namespace FileAccessGovernance.Shared;

/// <summary>
/// Single source of truth for path normalization — used by the Scan Agent (to
/// compute the PathHash it stages) and the Query API (to compute the PathHash it
/// looks up). They must never drift apart, or a scanned object becomes unreachable
/// by the exact path a caller queries with.
///
/// NTFS/SMB paths are case-insensitive along their entire length, not just the
/// host/share segment — an earlier draft of the design doc only uppercased the
/// share portion, which would have hashed "\Finance\Reports" and "\FINANCE\REPORTS"
/// to two different PathHash values for what is the same folder on disk.
/// </summary>
public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim().TrimEnd('\\').ToUpperInvariant();
    }
}
