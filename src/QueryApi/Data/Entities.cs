namespace FileAccessGovernance.QueryApi.Data;

public sealed class SecurityDescriptor
{
    public long DescriptorId { get; set; }
    public string DescriptorHash { get; set; } = default!;
    public string OwnerSid { get; set; } = default!;
    public string RawSddl { get; set; } = default!;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public sealed class SecurityDescriptorAce
{
    public long AceId { get; set; }
    public long DescriptorId { get; set; }
    public string TrusteeSid { get; set; } = default!;
    public byte AceType { get; set; } // 0 = Allow, 1 = Deny — see Shared.Models.AceType
    public int AccessMask { get; set; }
    public bool IsInherited { get; set; }
    public byte InheritanceFlags { get; set; }
}

public sealed class FsObject
{
    public long ObjectId { get; set; }
    public byte[] PathHash { get; set; } = default!;
    public string FullPath { get; set; } = default!;
    public byte[]? ParentPathHash { get; set; }
    public long? ParentObjectId { get; set; }
    public bool IsDirectory { get; set; }
    public long DescriptorId { get; set; }
    public bool IsInheritanceBreak { get; set; }
    public string ShareName { get; set; } = default!;
    public DateTime LastScannedUtc { get; set; }
}

public sealed class SidNameCacheEntry
{
    public string Sid { get; set; } = default!;
    public string? DisplayName { get; set; }
    public DateTime ResolvedUtc { get; set; }
}
