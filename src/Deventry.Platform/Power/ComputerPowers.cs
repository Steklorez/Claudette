using Deventry.Core.Power;
using Deventry.Core.Processes;
using Deventry.Platform.Power.Windows;

namespace Deventry.Platform.Power;

public static class ComputerPowers
{
    /// <summary>
    /// What shuts this computer down or puts it to sleep once Claude is done (DESIGN.md §9, "When Claude is done"): the
    /// system's own commands, and on Windows its sleep through <see cref="WindowsSleep"/>.
    /// </summary>
    public static IComputerPower CreateForCurrentOS(IProcessLauncher launcher, TimeProvider time)
    {
        if (OperatingSystem.IsWindows())
        {
            return new CommandComputerPower(PowerSystem.Windows, launcher, time, File.Exists, Environment.SystemDirectory, WindowsSleep.Run);
        }
        if (OperatingSystem.IsMacOS())
        {
            return new CommandComputerPower(PowerSystem.Mac, launcher, time, File.Exists);
        }
        if (OperatingSystem.IsLinux())
        {
            return new CommandComputerPower(PowerSystem.Linux, launcher, time, File.Exists);
        }
        return new NoComputerPower("Deventry only knows how on Windows, macOS and Linux.");
    }
}
