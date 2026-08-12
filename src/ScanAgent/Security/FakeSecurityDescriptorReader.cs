using FileAccessGovernance.Shared.Models;

namespace FileAccessGovernance.ScanAgent.Security;

/// <summary>
/// Returns canned data instead of calling real Win32 APIs — lets the rest of the
/// pipeline (queue processing, Kafka publishing, batching) be developed and tested
/// on a Mac/Linux dev machine, per design doc §7 point 6. Swapped in via DI when
/// not running on Windows — see Program.cs.
/// </summary>
public sealed class FakeSecurityDescriptorReader : ISecurityDescriptorReader
{
    public SecurityDescriptorInfo Read(string path, bool isDirectory)
    {
        var sddl = "O:S-1-5-21-1-1-1-512D:(A;;FA;;;S-1-5-21-1-1-1-513)";
        var aces = new List<AceRecord>
        {
            new("S-1-5-21-1-1-1-513", AceType.Allow, AccessMask: 2032127, IsInherited: false, AceInheritanceFlags.ContainerInherit | AceInheritanceFlags.ObjectInherit)
        };
        return new SecurityDescriptorInfo(sddl, "S-1-5-21-1-1-1-512", IsProtected: false, aces);
    }
}
