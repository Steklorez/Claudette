using Deventry.Core.Files;
using Deventry.Core.Processes;
using Deventry.Platform.Files.Linux;
using Deventry.Platform.Files.Mac;
using Deventry.Platform.Files.Windows;

namespace Deventry.Platform.Files;

/// <summary>
/// The OS's trash, for deleting from the Project Files tab's explorer (DESIGN.md §18, "Project files", "Renaming and
/// deleting"): Windows' Recycle Bin through the shell's file operation, the macOS Trash through <c>NSFileManager</c>, and
/// the desktop's trash on Linux through <c>gio trash</c>.
/// </summary>
public static class Trash
{
    public static ITrash CreateForCurrentOS(IProcessLauncher launcher, TimeProvider time) =>
        OperatingSystem.IsWindows() ? new WindowsTrash()
        : OperatingSystem.IsMacOS() ? new MacTrash()
        : OperatingSystem.IsLinux() ? new GioTrash(launcher, time)
        : new NoTrash();
}
