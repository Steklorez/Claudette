using Deventry.Core.Processes;
using Deventry.Core.Updates;
using Deventry.Platform.Notifications.Windows;
using Deventry.Platform.Updates.Mac;
using Deventry.Platform.Updates.Windows;
using Microsoft.Extensions.Logging;

namespace Deventry.Platform.Updates;

public static class AppInstallers
{
    /// <summary>
    /// The installer for how this Claudette was installed (DESIGN.md §2, "Updating Claudette"): the MSIX package on
    /// Windows, or <c>Deventry.app</c> on macOS. A package from the Microsoft Store is the Store's to update. Anything
    /// else, such as a Linux build or an unpackaged Windows one, can't install updates itself, and is offered the release
    /// page instead.
    /// </summary>
    /// <param name="updatesDirectory">Where downloads go, and on macOS where the image is mounted.</param>
    public static IAppInstaller CreateForCurrentOS(IProcessLauncher launcher, TimeProvider timeProvider, string updatesDirectory, ILogger logger)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) && WindowsAppIdentity.IsPackaged && Deployment.IsFromStore())
        {
            return new NoAppInstaller(AppInstallKind.Store);
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240) && WindowsAppIdentity.IsPackaged)
        {
            return new MsixInstaller(timeProvider, logger);
        }
        if (OperatingSystem.IsMacOS() && MacAppInstaller.FindBundle(AppContext.BaseDirectory) is { } bundle)
        {
            return new MacAppInstaller(launcher, timeProvider, bundle, updatesDirectory, Environment.ProcessId);
        }
        return new NoAppInstaller();
    }
}
