using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileAccessGovernance.ScanAgent.Security;

/// <summary>
/// Raw Win32 P/Invoke declarations. Compiles on any OS (these are just attributes
/// and signatures — no actual OS call happens at compile time), but every function
/// here only *works* on Windows, and specifically requires the calling process to
/// have SeBackupPrivilege enabled (see PrivilegeEnabler) for the backup-semantics
/// open below to bypass a locked-down object's own DACL — which is the entire
/// reason this project needs the privilege in the first place (main plan §3.A).
///
/// IMPORTANT CAVEAT: this file could not be compiled+run on a real Windows machine
/// in this environment (no Windows host was available) — it's written to documented
/// Win32/MS-DTYP behavior, but should get a focused verification pass on real
/// Windows before being trusted in production. See the accompanying summary.
/// </summary>
internal static class NativeMethods
{
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;

    // Bypasses the normal traverse/read access check for the caller's own DACL,
    // PROVIDED the caller's token has SeBackupPrivilege enabled — this is the
    // actual mechanism backup software (and this scanner) uses to read objects
    // the scanning account wouldn't otherwise have permission to open.
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // Don't follow symlinks/junctions when opening — avoids the infinite-loop risk
    // flagged in the main plan's risk list (§8).
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    internal const uint READ_CONTROL = 0x00020000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    internal enum SE_OBJECT_TYPE
    {
        SE_FILE_OBJECT = 1
    }

    [Flags]
    internal enum SECURITY_INFORMATION : uint
    {
        Owner = 0x00000001,
        Group = 0x00000002,
        Dacl = 0x00000004,
        Sacl = 0x00000008
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern uint GetSecurityInfo(
        SafeHandle handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        out IntPtr ppsidOwner,
        out IntPtr ppsidGroup,
        out IntPtr ppDacl,
        out IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr securityDescriptor,
        uint requestedStringSddlRevision,
        SECURITY_INFORMATION securityInformation,
        out IntPtr stringSecurityDescriptor,
        out uint stringSecurityDescriptorLen);

    internal const uint SDDL_REVISION_1 = 1;

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr hMem);

    // --- Privilege enabling (PrivilegeEnabler.cs) ---

    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privilege; // only ever adjusting one privilege at a time here
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLengthInBytes,
        IntPtr previousState,
        IntPtr returnLengthInBytes);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);
}
