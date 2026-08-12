using FileAccessGovernance.QueryApi.Data;
using FileAccessGovernance.QueryApi.Dtos;

namespace FileAccessGovernance.QueryApi.Services;

public static class FolderAccessResponseMapper
{
    public static FolderAccessResponse ToResponse(
        FsObject obj,
        IReadOnlyList<SecurityDescriptorAce> aces,
        IReadOnlyDictionary<string, string?> trusteeNames)
    {
        var entries = aces
            .Select(a => new AceEntryDto(
                TrusteeSid: a.TrusteeSid,
                TrusteeName: trusteeNames.GetValueOrDefault(a.TrusteeSid),
                AceType: a.AceType == 0 ? "Allow" : "Deny",
                AccessMask: a.AccessMask,
                IsInherited: a.IsInherited))
            .ToList();

        return new FolderAccessResponse(
            Path: obj.FullPath,
            IsDirectory: obj.IsDirectory,
            IsInheritanceBreak: obj.IsInheritanceBreak,
            LastScannedUtc: obj.LastScannedUtc,
            Entries: entries);
    }
}
