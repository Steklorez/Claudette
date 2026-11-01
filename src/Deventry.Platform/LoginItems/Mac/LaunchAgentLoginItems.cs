using System.Security;
using Deventry.Core.Files;
using Deventry.Core.LoginItems;
using Deventry.Core.Processes;
using Deventry.Platform.Updates.Mac;

namespace Deventry.Platform.LoginItems.Mac;

/// <summary>
/// Starting Deventry at login on macOS (DESIGN.md §9, "Starting at login"): a LaunchAgent in
/// <c>~/Library/LaunchAgents</c>, which launchd runs when the user logs in. It runs <c>open -a Deventry.app --args
/// --login</c> for the app, and a build's own program otherwise, or <c>dotnet Deventry.dll</c> when that program
/// wouldn't find .NET. A LaunchAgent works on macOS 12, which the app still supports, and for a source build, which has
/// no bundle; <c>SMAppService</c> needs macOS 13 and a bundle. Only file work, so it's tested on every OS.
/// </summary>
/// <param name="dotnet">The .NET a build is started with when its program wouldn't find it: the one Deventry runs on, by default.</param>
/// <param name="label">The agent's label and file name: Deventry's, unless it's another app's agent, such as Claudette's.</param>
/// <param name="bundleIdentifier">The app the agent belongs to, so Login Items names it after the app.</param>
public sealed class LaunchAgentLoginItems(
    IProcessLauncher launcher, string launchAgentsDirectory, LoginDotnet? dotnet = null, string label = LaunchAgentLoginItems.Label,
    string bundleIdentifier = MacAppInstaller.BundleIdentifier) : ILoginItems
{
    public const string Label = MacAppInstaller.BundleIdentifier + ".login";

    private readonly LoginDotnet _dotnet = dotnet ?? LoginDotnet.Current();

    public string AgentPath => Path.Combine(launchAgentsDirectory, label + ".plist");

    public string? UnavailableReason => null;

    public string SystemSettingsName => "System Settings → General → Login Items";

    public IPackageStartupTask? PackageTask => null;

    /// <summary>Whether the agent is there. Login Items can also turn it off under Allow in the Background, which isn't read.</summary>
    public LoginEntryState ReadEntry() => File.Exists(AgentPath) ? LoginEntryState.Enabled : LoginEntryState.Missing;

    public void WriteEntry(DeventryCopy copy)
    {
        Directory.CreateDirectory(launchAgentsDirectory);
        AtomicFile.WriteAllText(AgentPath, Plist(Command(copy), label, bundleIdentifier));
    }

    public void DeleteEntry()
    {
        if (File.Exists(AgentPath))
        {
            File.Delete(AgentPath);
        }
    }

    public bool Exists(DeventryCopy copy) => copy.ExistsOnDisk();

    public void Start(DeventryCopy copy)
    {
        var command = Command(copy);
        launcher.Start(new ProcessStartSpec(command.Program, command.Arguments) { Detached = true });
    }

    /// <summary>
    /// The agent: run once at login, in the user's graphical session. launchd would otherwise stop what the command
    /// starts when it exits, such as the copy a source build runs from, so the process group is left alone.
    /// </summary>
    public static string Plist(LoginCommand command, string label = Label, string bundleIdentifier = MacAppInstaller.BundleIdentifier)
    {
        var arguments = string.Concat(command.Arguments.Prepend(command.Program).Select(a => $"\n\t\t<string>{SecurityElement.Escape(a)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
            	<key>Label</key>
            	<string>{label}</string>
            	<key>ProgramArguments</key>
            	<array>{arguments}
            	</array>
            	<key>RunAtLoad</key>
            	<true/>
            	<key>LimitLoadToSessionType</key>
            	<string>Aqua</string>
            	<key>ProcessType</key>
            	<string>Interactive</string>
            	<key>AbandonProcessGroup</key>
            	<true/>
            	<key>AssociatedBundleIdentifiers</key>
            	<array>
            		<string>{bundleIdentifier}</string>
            	</array>
            </dict>
            </plist>

            """;
    }

    private LoginCommand Command(DeventryCopy copy) =>
        LoginCommand.For(copy, windows: false, _dotnet.Host, _dotnet.FoundByProgram)
        ?? throw new InvalidOperationException("An MSIX can't start on macOS.");
}
