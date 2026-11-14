using Deventry.Core.Processes;
using Deventry.Core.TimedMessages;
using Deventry.Platform.Power.Windows;

namespace Deventry.Platform.Power;

public static class WakeTimers
{
    /// <summary>
    /// What wakes this computer for the next timed message (DESIGN.md §18, "Timed messages", "Waking the computer"): a
    /// waitable timer on Windows, which any program may set.
    /// </summary>
    public static IWakeTimer CreateForCurrentOS(IProcessLauncher launcher, TimeProvider time)
    {
        _ = launcher;
        _ = time;
        if (OperatingSystem.IsWindows())
        {
            return new WindowsWakeTimer();
        }
        return new NoWakeTimer("Deventry can't wake this computer.");
    }
}
