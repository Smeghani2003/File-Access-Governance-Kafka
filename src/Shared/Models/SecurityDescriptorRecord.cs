namespace FileAccessGovernance.Shared.Models;

/// <summary>
/// Full descriptor payload — only attached to an ObjectRecord the first time an
/// agent encounters a given DescriptorHash in the current scan run. Existing
/// descriptors don't need to be re-sent; the merge procedure only needs the hash
/// to associate the object with a descriptor it already has. See design doc §5.1.
/// </summary>
public sealed record SecurityDescriptorRecord(
    string DescriptorHash,
    string OwnerSid,
    string RawSddl,
    IReadOnlyList<AceRecord> Aces);
