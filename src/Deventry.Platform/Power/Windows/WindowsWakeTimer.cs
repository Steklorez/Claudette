using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Deventry.Core.TimedMessages;
using Microsoft.Win32.SafeHandles;

namespace Deventry.Platform.Power.Windows;

/// <summary>
/// Wakes the computer for the next timed message with a waitable timer that resumes the system
/// (<c>SetWaitableTimer(…, fResume: TRUE)</c>; DESIGN.md §18, "Timed messages", "Waking the computer"). Any program may
/// set one; Windows wakes for it if the power plan's <b>Allow wake timers</b> is on, which <see cref="Caveat"/> reads.
/// The timer is Deventry's handle, so it goes when Deventry closes, and nothing wakes the computer for a message then.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsWakeTimer : IWakeTimer
{
    private const uint TimerAllAccess = 0x1F0003;

    /// <summary>The sleep settings' group, and in it <b>Allow wake timers</b>: 0 off, 1 on, 2 important ones only.</summary>
    private static readonly Guid SleepGroup = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    private static readonly Guid AllowWakeTimers = new("BD3B718A-0680-4D9D-8AB2-E1D2B4AC806D");

    private readonly Lock _lock = new();
    private SafeWaitHandle? _timer;
    private DateTimeOffset? _at;
    private string? _error;

    public WakeSetup Setup => WakeSetup.NotNeeded;

    public string? UnavailableReason => null;

    public string? Caveat => WindowsPowerPlan.CaveatFor(ReadAllowWakeTimers(plugged: true), ReadAllowWakeTimers(plugged: false), HasBattery());

    public DateTimeOffset? WakesAt
    {
        get
        {
            lock (_lock)
            {
                return _at;
            }
        }
    }

    public Task WakeAtAsync(DateTimeOffset? at, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (at is not { } time)
            {
                if (_timer is { IsInvalid: false } timer)
                {
                    CancelWaitableTimer(timer);
                }
                _at = null;
                return Task.CompletedTask;
            }
            _timer ??= CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
            if (_timer.IsInvalid)
            {
                _error = $"Windows didn't make a wake timer (error {Marshal.GetLastPInvokeError()}).";
                _at = null;
                return Task.CompletedTask;
            }
            // A positive due time is when, in UTC, as a FILETIME.
            var due = time.UtcDateTime.ToFileTimeUtc();
            if (SetWaitableTimer(_timer, in due, 0, 0, 0, resume: true))
            {
                _at = time;
                _error = null;
            }
            else
            {
                _error = $"Windows refused the wake timer (error {Marshal.GetLastPInvokeError()}).";
                _at = null;
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> InstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> UninstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public string Describe()
    {
        lock (_lock)
        {
            var set = _error ?? (_at is { } at ? $"A Windows wake timer is set for {at.UtcDateTime:yyyy-MM-dd HH:mm} UTC." : "No wake timer is set.");
            return Caveat is { } caveat ? $"{set} {caveat}" : set;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_timer is { IsInvalid: false } timer)
            {
                CancelWaitableTimer(timer);
            }
            _timer?.Dispose();
            _timer = null;
            _at = null;
        }
    }

    private static uint? ReadAllowWakeTimers(bool plugged)
    {
        try
        {
            if (PowerGetActiveScheme(0, out var schemePointer) != 0)
            {
                return null;
            }
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(schemePointer);
                var group = SleepGroup;
                var setting = AllowWakeTimers;
                var result = plugged
                    ? PowerReadACValueIndex(0, in scheme, in group, in setting, out var value)
                    : PowerReadDCValueIndex(0, in scheme, in group, in setting, out value);
                return result == 0 ? value : null;
            }
            finally
            {
                LocalFree(schemePointer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Whether the computer has a battery: a desktop's plan on battery doesn't matter.</summary>
    private static bool HasBattery() => GetSystemPowerStatus(out var status) && status.BatteryFlag != 128;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelWaitableTimer(SafeWaitHandle timer);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(nint root, out nint scheme);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadDCValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, out uint value);
}

/// <summary>What Windows' power plan says of wake timers, in words: pure, so tested on every system.</summary>
internal static class WindowsPowerPlan
{
    /// <summary>
    /// What Settings and the cup say when the power plan won't wake for Deventry: <b>Allow wake timers</b> off or only for
    /// important ones, plugged in or, with a battery, on it. Null when it will.
    /// </summary>
    internal static string? CaveatFor(uint? plugged, uint? onBattery, bool hasBattery)
    {
        const string Where = "Allow wake timers is in Control Panel → Power Options → Change plan settings → Change advanced power settings → Sleep.";
        // 2 is "important wake timers only", which a program's aren't.
        var pluggedIn = plugged is null or 1;
        var battery = onBattery is null or 1;
        if (pluggedIn && (battery || !hasBattery))
        {
            return null;
        }
        if (!pluggedIn && (!battery || !hasBattery))
        {
            return $"Windows' power plan doesn't let programs wake the computer, so it won't wake for them. {Where}";
        }
        return pluggedIn
            ? $"Windows' power plan lets programs wake the computer only while it's plugged in. {Where}"
            : $"Windows' power plan doesn't let programs wake the computer while it's plugged in. {Where}";
    }
}
