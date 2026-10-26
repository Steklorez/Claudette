using Claudette.Core.Files;
using Claudette.Core.Processes;
using Claudette.Platform.Files.Linux;
using Claudette.Platform.Files.Mac;
using Claudette.Platform.Files.Windows;

namespace Claudette.Platform.Files;

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
