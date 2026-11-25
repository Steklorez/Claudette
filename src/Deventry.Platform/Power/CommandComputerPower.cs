using Deventry.Core.Power;
using Deventry.Core.Processes;
using Deventry.Platform.Presence;

namespace Deventry.Platform.Power;

internal enum PowerSystem
{
    Windows,
    Mac,
    Linux,
}

/// <summary>
/// Shuts the computer down or puts it to sleep by running the system's own commands (DESIGN.md §9, "When Claude is
/// done"): <c>shutdown.exe /s /t 0</c> on Windows, whose sleep is <paramref name="windowsSleep"/>; System Events'
/// <c>shut down</c> and <c>pmset sleepnow</c> on macOS; <c>systemctl poweroff</c> and <c>suspend</c> on Linux. None
/// forces anything, so an app with unsaved work can still hold a shutdown up.
/// </summary>
/// <param name="fileExists">Whether a file is there, for the commands Linux may not have.</param>
/// <param name="windowsSleep">Windows' sleep, which is no command: null once asked, or why it couldn't be.</param>
internal sealed class CommandComputerPower(PowerSystem system, IProcessLauncher launcher, TimeProvider time, Func<string, bool> fileExists,
    string windowsSystemDirectory = "", Func<string?>? windowsSleep = null) : IComputerPower
{
    /// <summary>How long a command has to say it has asked: shutting down itself takes as long as it takes.</summary>
    internal static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private const string OsaScript = "/usr/bin/osascript";
    private const string Gdbus = "/usr/bin/gdbus";

    /// <summary>The script that shuts a Mac down, as the Apple menu's Shut Down does, asking each app to quit.</summary>
    internal const string MacShutDown = "tell application \"System Events\" to shut down";

    /// <summary>What asks macOS's permission to send System Events anything, without doing anything with it.</summary>
    internal const string MacAskPermission = "tell application \"System Events\" to get name";

    /// <summary>What macOS says when the user hasn't let Deventry send System Events anything: <c>errAEEventNotPermitted</c>.</summary>
    private const string NotPermitted = "-1743";

    private string? Systemctl => new[] { "/usr/bin/systemctl", "/bin/systemctl" }.FirstOrDefault(fileExists);

    public string? UnavailableReason(WhenDone action) => action switch
    {
        WhenDone.Nothing => null,
        _ when system == PowerSystem.Linux && Systemctl is null => "systemctl wasn't found: Deventry shuts Linux down and puts it to sleep through systemd.",
        _ when system == PowerSystem.Windows && action == WhenDone.Sleep && windowsSleep is null => "Deventry can't put this computer to sleep.",
        _ => null,
    };

    /// <summary>The command that does <paramref name="action"/>, or null for Windows' sleep, which is no command.</summary>
    internal ProcessStartSpec? Command(WhenDone action) => (system, action) switch
    {
        (PowerSystem.Windows, WhenDone.ShutDown) => new ProcessStartSpec(Path.Combine(windowsSystemDirectory, "shutdown.exe"), ["/s", "/t", "0"]),
        (PowerSystem.Mac, WhenDone.ShutDown) => new ProcessStartSpec(OsaScript, ["-e", MacShutDown]),
        (PowerSystem.Mac, WhenDone.Sleep) => new ProcessStartSpec("/usr/bin/pmset", ["sleepnow"]),
        (PowerSystem.Linux, WhenDone.ShutDown) => new ProcessStartSpec(Systemctl ?? "systemctl", ["poweroff"]),
        (PowerSystem.Linux, WhenDone.Sleep) => new ProcessStartSpec(Systemctl ?? "systemctl", ["suspend"]),
        _ => null,
    };

    public async Task<string?> PrepareAsync(WhenDone action, CancellationToken cancellationToken = default)
    {
        if (UnavailableReason(action) is { } reason)
        {
            return reason;
        }
        try
        {
            return (system, action) switch
            {
                // macOS asks the user once whether Deventry may tell System Events anything: now, while they're here.
                (PowerSystem.Mac, WhenDone.ShutDown) => await AskMacAsync(cancellationToken).ConfigureAwait(false),
                (PowerSystem.Linux, _) => await AskLogindAsync(action, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            // What couldn't be asked is tried when it's time.
            return null;
        }
    }

    private async Task<string?> AskMacAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(launcher, new ProcessStartSpec(OsaScript, ["-e", MacAskPermission]), RunTimeout, time, cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0 ? null
            : result.StandardError.Contains(NotPermitted, StringComparison.Ordinal)
                ? "macOS doesn't let Deventry tell System Events to shut down: allow it in System Settings → Privacy & Security → Automation, under Deventry."
                : $"macOS didn't let Deventry reach System Events: {result.StandardError.Trim()}";
    }

    /// <summary>Whether logind lets this session shut down or suspend without a password, which nobody may be there to type.</summary>
    private async Task<string?> AskLogindAsync(WhenDone action, CancellationToken cancellationToken)
    {
        if (!fileExists(Gdbus))
        {
            return null;
        }
        var method = action == WhenDone.ShutDown ? "CanPowerOff" : "CanSuspend";
        var result = await ProcessRunner.RunAsync(launcher, new ProcessStartSpec(Gdbus,
            ["call", "--system", "--dest", "org.freedesktop.login1", "--object-path", "/org/freedesktop/login1", "--method", $"org.freedesktop.login1.Manager.{method}"]),
            RunTimeout, time, cancellationToken).ConfigureAwait(false);
        var what = action == WhenDone.ShutDown ? "shut down" : "go to sleep";
        return result.ExitCode != 0 ? null : GdbusReply.Text(result.StandardOutput) switch
        {
            "yes" or null => null,
            "challenge" => $"Linux asks for a password before this computer can {what}, which nobody may be there to type.",
            _ => $"Linux doesn't let this session {what}.",
        };
    }

    public async Task<string?> RunAsync(WhenDone action, CancellationToken cancellationToken = default)
    {
        if (UnavailableReason(action) is { } reason)
        {
            return reason;
        }
        if (Command(action) is not { } command)
        {
            return action == WhenDone.Sleep && windowsSleep is not null ? windowsSleep() : null;
        }
        try
        {
            var result = await ProcessRunner.RunAsync(launcher, command, RunTimeout, time, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? null
                : $"{Path.GetFileName(command.FileName)} didn't (exit code {result.ExitCode}){(result.StandardError.Trim() is { Length: > 0 } error ? $": {error}" : ".")}";
        }
        catch (TimeoutException)
        {
            // Still going after this long: the system has it, and is shutting down or going to sleep.
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"{Path.GetFileName(command.FileName)} couldn't run: {ex.Message}";
        }
    }
}
