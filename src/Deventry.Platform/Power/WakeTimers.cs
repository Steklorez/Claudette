using Deventry.Core.Processes;
using Deventry.Core.TimedMessages;
using Deventry.Platform.Power.Windows;

namespace Deventry.Platform.Power;

public static class WakeTimers
{
    /// <summary>
    /// What wakes this computer for the next timed message (DESIGN.md §18, "Timed messages", "Waking the computer"): a
    /// waitable timer on Windows, which any program may set, and on macOS and Linux the wake helper, once installed.
    /// </summary>
    public static IWakeTimer CreateForCurrentOS(IProcessLauncher launcher, TimeProvider time)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsWakeTimer();
        }
        if (OperatingSystem.IsMacOS())
        {
            return HelperWakeTimer.ForMac(launcher, time);
        }
        if (OperatingSystem.IsLinux())
        {
            return HelperWakeTimer.ForLinux(launcher, time);
        }
        return new NoWakeTimer("Deventry can't wake this computer.");
    }
}
