using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Deventry.Platform.Power.Windows;

/// <summary>
/// Puts a Windows computer to sleep, as its Start menu's Sleep does (DESIGN.md §9, "When Claude is done"): one with S3
/// through <c>SetSuspendState</c>, which needs the shutdown privilege enabled first; one with Modern Standby (S0 low
/// power idle, which has no S3) by turning its display off, which is how such a computer goes to sleep.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsSleep
{
    /// <summary>Larger than <c>SYSTEM_POWER_CAPABILITIES</c>, 76 bytes.</summary>
    private const int CapabilitiesSize = 128;

    private const int SystemS3 = 5;

    /// <summary><c>AoAc</c>, "always on, always connected": Modern Standby.</summary>
    private const int AlwaysOnAlwaysConnected = 20;

    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint PrivilegeEnabled = 2;
    private const nint Broadcast = 0xFFFF;
    private const uint SystemCommand = 0x0112;
    private const nint MonitorPower = 0xF170;
    private const nint MonitorOff = 2;

    /// <summary>Null once Windows has been asked, or why it couldn't be.</summary>
    public static string? Run()
    {
        var capabilities = stackalloc byte[CapabilitiesSize];
        if (GetPwrCapabilities(capabilities) == 0)
        {
            return $"Windows didn't say how this computer sleeps (error {Marshal.GetLastPInvokeError()}).";
        }
        if (capabilities[SystemS3] != 0)
        {
            return Suspend();
        }
        if (capabilities[AlwaysOnAlwaysConnected] != 0)
        {
            return PostMessageW(Broadcast, SystemCommand, MonitorPower, MonitorOff) ? null : $"Windows didn't turn the display off (error {Marshal.GetLastPInvokeError()}).";
        }
        return "This computer has no sleep that Windows lets Deventry use.";
    }

    private static string? Suspend()
    {
        if (EnableShutdownPrivilege() is { } error)
        {
            return error;
        }
        return SetSuspendState(hibernate: 0, force: 0, wakeupEventsDisabled: 0) != 0 ? null : $"Windows didn't put this computer to sleep (error {Marshal.GetLastPInvokeError()}).";
    }

    /// <summary><c>SetSuspendState</c> needs <c>SeShutdownPrivilege</c>, which a signed-in user has but not enabled.</summary>
    private static string? EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return $"Deventry couldn't ask Windows for the right to sleep (error {Marshal.GetLastPInvokeError()}).";
        }
        try
        {
            if (!LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid))
            {
                return $"Deventry couldn't ask Windows for the right to sleep (error {Marshal.GetLastPInvokeError()}).";
            }
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
            // It succeeds without enabling it when the right isn't held: the last error says so.
            if (!AdjustTokenPrivileges(token, false, &privileges, 0, null, null) || Marshal.GetLastPInvokeError() != 0)
            {
                return $"Windows doesn't let Deventry put this computer to sleep (error {Marshal.GetLastPInvokeError()}).";
            }
            return null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary><c>TOKEN_PRIVILEGES</c> with its one <c>LUID_AND_ATTRIBUTES</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Luid;
        public uint Attributes;
    }

    [LibraryImport("powrprof.dll", SetLastError = true)]
    private static partial byte GetPwrCapabilities(byte* capabilities);

    [LibraryImport("powrprof.dll", SetLastError = true)]
    private static partial byte SetSuspendState(byte hibernate, byte force, byte wakeupEventsDisabled);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? system, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TokenPrivileges* state, uint length, TokenPrivileges* previous, uint* returned);
}
