namespace FileAccessGovernance.QueryApi.Dtos;

public sealed record AceEntryDto(
    string TrusteeSid,
    string? TrusteeName,
    string AceType,      // "Allow" | "Deny"
    int AccessMask,
    bool IsInherited);

public sealed record FolderAccessResponse(
    string Path,
    bool IsDirectory,
    bool IsInheritanceBreak,
    DateTime LastScannedUtc,
    IReadOnlyList<AceEntryDto> Entries);

public sealed record ObjectDto(
    long ObjectId,
    string Path,
    long? ParentObjectId,
    bool IsDirectory,
    long DescriptorId,
    DateTime LastScannedUtc);

public sealed record ErrorEnvelope(ErrorDetail Error);

public sealed record ErrorDetail(string Code, string Message);
