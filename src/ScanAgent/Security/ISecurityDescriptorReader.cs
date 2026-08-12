using FileAccessGovernance.Shared.Models;

namespace FileAccessGovernance.ScanAgent.Security;

/// <param name="IsProtected">
/// True when the object's DACL is marked "protected" (SDDL control flag "P" /
/// SE_DACL_PROTECTED — what Windows Explorer calls "disable inheritance"). This,
/// not "does it have any explicit ACEs", is the correct signal for
/// FsObjects.IsInheritanceBreak: an object can carry explicit ACEs *in addition to*
/// still inheriting from its parent (very common — e.g. one extra Deny layered on
/// top), so explicit-ACE presence alone would over-count inheritance breaks.
/// </param>
public sealed record SecurityDescriptorInfo(string RawSddl, string OwnerSid, bool IsProtected, IReadOnlyList<AceRecord> Aces);

/// <summary>
/// Abstraction over reading an object's security descriptor, so the rest of the
/// agent (queue processing, Kafka publishing) can be developed and tested on a
/// non-Windows machine using FakeSecurityDescriptorReader — see design doc §7.
/// </summary>
public interface ISecurityDescriptorReader
{
    SecurityDescriptorInfo Read(string path, bool isDirectory);
}
