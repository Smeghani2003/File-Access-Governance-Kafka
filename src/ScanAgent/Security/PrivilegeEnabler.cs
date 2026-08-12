using System.Runtime.InteropServices;

namespace FileAccessGovernance.ScanAgent.Security;

/// <summary>
/// Windows privileges are present-but-disabled in a process token by default, even
/// for an account that's been granted them — this has to run once at startup
/// (before any file access) to actually enable SeBackupPrivilege for this process.
/// Granting the privilege to the service account (Local Security Policy) alone is
/// NOT sufficient — see design doc §1 for the full explanation of why this was a
/// gap in an earlier draft.
/// </summary>
public static class PrivilegeEnabler
{
    public static void EnableBackupPrivilege()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Local dev on a non-Windows machine — see design doc §7. There is
            // nothing to enable, and nothing downstream should be calling real
            // Win32 file APIs in this case anyway (ISecurityDescriptorReader
            // should be swapped for FakeSecurityDescriptorReader).
            return;
        }

        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out var tokenHandle))
        {
            throw new InvalidOperationException(
                $"OpenProcessToken failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValueW(null, "SeBackupPrivilege", out var luid))
            {
                throw new InvalidOperationException(
                    $"LookupPrivilegeValueW failed for SeBackupPrivilege (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            var privileges = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privilege = new NativeMethods.LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                }
            };

            if (!NativeMethods.AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new InvalidOperationException(
                    $"AdjustTokenPrivileges failed (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            // AdjustTokenPrivileges can return true but still not actually enable the
            // privilege if the account was never granted it — that specific failure
            // mode reports success with GetLastError() == ERROR_NOT_ALL_ASSIGNED (1300).
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == 1300)
            {
                throw new InvalidOperationException(
                    "AdjustTokenPrivileges reported ERROR_NOT_ALL_ASSIGNED — the service account " +
                    "was never actually granted SeBackupPrivilege in Local Security Policy. " +
                    "Enabling the privilege in code cannot substitute for granting it to the account.");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }
}
