using System.Diagnostics;
using Deventry.Core;
using Deventry.Core.LoginItems;
using Deventry.Core.Processes;
using Deventry.Core.Settings;
using Deventry.Core.Updates;
using Deventry.Platform.LoginItems;

namespace Deventry.App.Services;

/// <summary>Starting Deventry at login (DESIGN.md §9, "Starting at login"), as far as launching goes.</summary>
public static class LoginLaunch
{
    /// <summary>
    /// The OS's login entry. A Deventry started with <c>DEVENTRY_HOME</c> gets none: the entry would start Deventry
    /// without it, on the real profile.
    /// </summary>
    public static ILoginItems CreateItems(IProcessLauncher launcher, TimeProvider timeProvider) =>
        // FromClaudette: CLAUDETTE_HOME too.
        Core.FromClaudette.ClaudetteVariables.Get(AppPaths.HomeVariable) is not null
            ? new NoLoginItems("Not while DEVENTRY_HOME is set: Deventry would start at login without it.")
            : LoginItemPlatforms.CreateForCurrentOS(launcher, timeProvider);

    /// <summary>This copy of Deventry: a source build by its build output, anything else as it's installed.</summary>
    public static DeventryCopy CurrentCopy(IReadOnlyList<string> args, AppVersion version) =>
        DevelopmentLaunch.Current(args) is { } build
            ? new DeventryCopy(AppInstallKind.SourceBuild, Path.TrimEndingDirectorySeparator(build.SourceOutput), version.ToString())
            : LoginItemPlatforms.CurrentCopy(version.ToString());

    /// <summary>
    /// For a launch with <c>--login</c>: when a better copy of Deventry is installed (the release, for a source build),
    /// starts it instead and returns true, so this one exits. That's how a source build's entry starts the MSIX, whose
    /// own startup task only the MSIX can turn on.
    /// </summary>
    public static bool TryHandOver(IReadOnlyList<string> args, AppPaths paths, IProcessLauncher launcher)
    {
        try
        {
            var items = CreateItems(launcher, TimeProvider.System);
            var state = new JsonFileStore<AppState>(paths.StateFile).Load();
            var startAtLogin = new StartAtLogin(items, CurrentCopy(args, AppServices.BuiltVersion()), state.LoginItem, save: () => { });
            if (startAtLogin.HandOverAtLogin() is not { } better)
            {
                return false;
            }
            items.Start(better);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Trace.WriteLine($"Couldn't hand over to the installed Deventry at login, so starting this one: {ex}");
            return false;
        }
    }
}
