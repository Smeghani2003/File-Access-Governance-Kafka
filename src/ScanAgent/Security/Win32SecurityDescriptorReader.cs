using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using FileAccessGovernance.Shared.Models;
using SharedAceType = FileAccessGovernance.Shared.Models.AceType;

namespace FileAccessGovernance.ScanAgent.Security;

/// <summary>
/// Real implementation: opens the object with FILE_FLAG_BACKUP_SEMANTICS (which, combined
/// with SeBackupPrivilege being enabled on this process — see PrivilegeEnabler — bypasses
/// the object's own DACL for the open itself, the actual mechanism that lets this scanner
/// read security descriptors on objects the scanning account wouldn't otherwise have
/// permission to open), then reads the descriptor via GetSecurityInfo.
///
/// NOTE ON SCOPE: this handles the security-descriptor read. Directory *enumeration*
/// (listing a folder's children) in ScanWorker uses the standard .NET Directory APIs,
/// which only need ordinary FILE_LIST_DIRECTORY access — sufficient for the common
/// case. A folder locked down enough to deny even listing to the scanning account is
/// a residual edge case that would need the same backup-semantics treatment applied
/// to enumeration (via NtQueryDirectoryFile), which is intentionally out of scope for
/// this Phase 1 MVP — see design doc §9.
///
/// COULD NOT BE RUN OR COMPILE-VERIFIED ON WINDOWS in this environment (no Windows
/// host was available) — written to documented Win32/MS-DTYP behavior; treat as
/// needing a focused verification pass on a real Windows machine before production use.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32SecurityDescriptorReader : ISecurityDescriptorReader
{
    public SecurityDescriptorInfo Read(string path, bool isDirectory)
    {
        using var handle = NativeMethods.CreateFileW(
            path,
            NativeMethods.READ_CONTROL,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException($"CreateFileW failed for '{path}' (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        const NativeMethods.SECURITY_INFORMATION securityInfo =
            NativeMethods.SECURITY_INFORMATION.Owner | NativeMethods.SECURITY_INFORMATION.Dacl;

        var status = NativeMethods.GetSecurityInfo(
            handle, NativeMethods.SE_OBJECT_TYPE.SE_FILE_OBJECT, securityInfo,
            out _, out _, out _, out _, out var pSecurityDescriptor);

        if (status != 0) // anything other than ERROR_SUCCESS
        {
            throw new IOException($"GetSecurityInfo failed for '{path}' (Win32 error {status}).");
        }

        try
        {
            if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    pSecurityDescriptor, NativeMethods.SDDL_REVISION_1, securityInfo,
                    out var pSddl, out _))
            {
                throw new IOException(
                    $"ConvertSecurityDescriptorToStringSecurityDescriptorW failed for '{path}' (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            try
            {
                var sddl = Marshal.PtrToStringUni(pSddl)
                    ?? throw new IOException($"GetSecurityInfo returned an empty SDDL string for '{path}'.");
                return ParseSddl(sddl);
            }
            finally
            {
                NativeMethods.LocalFree(pSddl);
            }
        }
        finally
        {
            NativeMethods.LocalFree(pSecurityDescriptor);
        }
    }

    private static SecurityDescriptorInfo ParseSddl(string sddl)
    {
        // RawSecurityDescriptor is pure managed parsing of SDDL text — no OS call
        // involved here, unlike the CreateFileW/GetSecurityInfo steps above.
        var descriptor = new RawSecurityDescriptor(sddl);
        var ownerSid = descriptor.Owner?.Value ?? string.Empty;

        var aces = new List<AceRecord>();
        if (descriptor.DiscretionaryAcl is not null)
        {
            foreach (var genericAce in descriptor.DiscretionaryAcl)
            {
                if (genericAce is not CommonAce ace) continue; // skip non-standard ACE types (rare in practice)

                aces.Add(new AceRecord(
                    TrusteeSid: ace.SecurityIdentifier.Value,
                    AceType: ace.AceType == System.Security.AccessControl.AceType.AccessDenied ? SharedAceType.Deny : SharedAceType.Allow,
                    AccessMask: ace.AccessMask,
                    IsInherited: (ace.AceFlags & AceFlags.Inherited) != 0,
                    InheritanceFlags: ToInheritanceFlags(ace.AceFlags)));
            }
        }

        var isProtected = descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected);
        return new SecurityDescriptorInfo(sddl, ownerSid, isProtected, aces);
    }

    private static AceInheritanceFlags ToInheritanceFlags(AceFlags flags)
    {
        var result = AceInheritanceFlags.None;
        if ((flags & AceFlags.ContainerInherit) != 0) result |= AceInheritanceFlags.ContainerInherit;
        if ((flags & AceFlags.ObjectInherit) != 0) result |= AceInheritanceFlags.ObjectInherit;
        if ((flags & AceFlags.InheritOnly) != 0) result |= AceInheritanceFlags.InheritOnly;
        if ((flags & AceFlags.NoPropagateInherit) != 0) result |= AceInheritanceFlags.NoPropagateInherit;
        return result;
    }
}
