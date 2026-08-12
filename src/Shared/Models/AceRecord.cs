namespace FileAccessGovernance.Shared.Models;

public enum AceType : byte
{
    Allow = 0,
    Deny = 1
}

[Flags]
public enum AceInheritanceFlags : byte
{
    None = 0,
    ContainerInherit = 1,
    ObjectInherit = 2,
    InheritOnly = 4,
    NoPropagateInherit = 8
}

/// <summary>
/// One Access Control Entry, matching dbo.SecurityDescriptorAces / #SecurityDescriptorAcesStaging.
/// </summary>
public sealed record AceRecord(
    string TrusteeSid,
    AceType AceType,
    int AccessMask,
    bool IsInherited,
    AceInheritanceFlags InheritanceFlags);
