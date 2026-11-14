using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Deventry.Core.Processes;
using Deventry.Core.TimedMessages;

namespace Deventry.Platform.Power;

/// <summary>
/// Wakes the computer for the next timed message through a small helper the user installs once with an administrator's
/// password (DESIGN.md §18, "Timed messages", "Waking the computer"), since setting a wake takes root on macOS and Linux:
/// <list type="bullet">
/// <item><b>macOS</b>: a root-owned script in <c>/Library/PrivilegedHelperTools</c> that runs <c>pmset schedule wake</c>
/// under the owner <i>Deventry</i>, cancelling its last one first; and a sudoers rule that lets the user who installed it
/// run that script, and nothing else, without a password. Installed through <c>osascript</c>'s <c>do shell script … with
/// administrator privileges</c>, run with <c>sudo -n</c>.</item>
/// <item><b>Linux</b>: a root-owned script in <c>/usr/local/libexec</c> that runs <c>rtcwake -m no</c>, and a polkit action
/// for that script alone, allowed for the active session's user without a password. Installed and run with
/// <c>pkexec</c>.</item>
/// </list>
/// Each script takes only <c>set &lt;seconds since 1970&gt;</c>, within the next 31 days, or <c>clear</c>. The script and
/// the rule go to root inside the one command the password is asked for, encoded, never through a file the user could
/// change before root copies it. When Deventry closes, its wake is cleared: a message isn't sent while Deventry is closed.
/// </summary>
public sealed class HelperWakeTimer : IWakeTimer
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Installing waits for the user to type the password.</summary>
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    private readonly WakeHelperSystem _system;
    private readonly IProcessLauncher _launcher;
    private readonly TimeProvider _time;
    private readonly Func<string, bool> _exists;
    private readonly string _user;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly Lock _lock = new();
    private DateTimeOffset? _at;
    private string? _error;
    private bool _disposed;

    /// <param name="exists">Whether a file is there: the helper, its rule, and the tools it runs.</param>
    /// <param name="user">The user the macOS rule is for.</param>
    public HelperWakeTimer(WakeHelperSystem system, IProcessLauncher launcher, TimeProvider time, Func<string, bool>? exists = null, string? user = null)
    {
        _system = system;
        _launcher = launcher;
        _time = time;
        _exists = exists ?? File.Exists;
        _user = user ?? Environment.UserName;
    }

    public static HelperWakeTimer ForMac(IProcessLauncher launcher, TimeProvider time) => new(WakeHelperSystem.Mac, launcher, time);

    public static HelperWakeTimer ForLinux(IProcessLauncher launcher, TimeProvider time) => new(WakeHelperSystem.Linux, launcher, time);

    /// <summary>Where the helper is.</summary>
    public string HelperPath => _system == WakeHelperSystem.Mac ? "/Library/PrivilegedHelperTools/app.deventry.wake" : "/usr/local/libexec/deventry-wake";

    /// <summary>The rule that lets the user run it without a password.</summary>
    public string RulePath => _system == WakeHelperSystem.Mac ? "/etc/sudoers.d/deventry-wake" : "/usr/share/polkit-1/actions/app.deventry.wake.policy";

    private const string MacState = "/Library/PrivilegedHelperTools/app.deventry.wake.last";

    private static readonly string[] RtcWakes = ["/usr/sbin/rtcwake", "/sbin/rtcwake", "/usr/bin/rtcwake"];

    public string? UnavailableReason =>
        _system == WakeHelperSystem.Linux && !_exists("/usr/bin/pkexec") ? "Deventry can't wake this computer: pkexec isn't installed."
        : _system == WakeHelperSystem.Linux && !RtcWakes.Any(_exists) ? "Deventry can't wake this computer: rtcwake isn't installed."
        : _system == WakeHelperSystem.Mac && !IsSafeUser(_user) ? $"Deventry can't wake this computer for the user {_user}."
        : null;

    public WakeSetup Setup =>
        UnavailableReason is not null ? WakeSetup.Unavailable
        : _exists(HelperPath) && _exists(RulePath) ? WakeSetup.HelperInstalled
        : WakeSetup.NeedsHelper;

    public string? Caveat => null;

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

    public async Task WakeAtAsync(DateTimeOffset? at, CancellationToken cancellationToken = default)
    {
        if (Setup != WakeSetup.HelperInstalled)
        {
            lock (_lock)
            {
                _at = null;
            }
            return;
        }
        await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }
            string[] arguments = at is { } time && time > _time.GetUtcNow()
                ? ["set", time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)]
                : ["clear"];
            var result = await RunHelperAsync(arguments, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _error = result.ExitCode == 0 ? null : $"The wake helper failed (exit code {result.ExitCode}): {result.StandardError.Trim()}";
                _at = result.ExitCode == 0 && arguments[0] == "set" ? at : null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            lock (_lock)
            {
                _error = $"The wake helper couldn't run: {ex.Message}";
                _at = null;
            }
        }
        finally
        {
            _running.Release();
        }
    }

    private Task<ProcessResult> RunHelperAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _system == WakeHelperSystem.Mac
            ? ProcessRunner.RunAsync(_launcher, new ProcessStartSpec("/usr/bin/sudo", ["-n", HelperPath, .. arguments]), RunTimeout, _time, cancellationToken)
            : ProcessRunner.RunAsync(_launcher, new ProcessStartSpec("/usr/bin/pkexec", [HelperPath, .. arguments]), RunTimeout, _time, cancellationToken);

    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (UnavailableReason is not null)
        {
            return false;
        }
        await RunAsRootAsync(InstallScript(), cancellationToken).ConfigureAwait(false);
        if (Setup != WakeSetup.HelperInstalled)
        {
            return false;
        }
        if (_system == WakeHelperSystem.Mac)
        {
            // An /etc/sudoers that doesn't read /etc/sudoers.d would leave the rule unread: sudo is asked.
            var check = await ProcessRunner.RunAsync(_launcher, new ProcessStartSpec("/usr/bin/sudo", ["-n", "-l", HelperPath]), RunTimeout, _time, cancellationToken)
                .ConfigureAwait(false);
            if (check.ExitCode != 0)
            {
                lock (_lock)
                {
                    _error = "The wake helper is installed, but sudo won't run it without a password: /etc/sudoers may not read /etc/sudoers.d.";
                }
                return false;
            }
        }
        return true;
    }

    public async Task<bool> UninstallAsync(CancellationToken cancellationToken = default)
    {
        await WakeAtAsync(null, cancellationToken).ConfigureAwait(false);
        await RunAsRootAsync(UninstallScript(), cancellationToken).ConfigureAwait(false);
        return Setup != WakeSetup.HelperInstalled;
    }

    /// <summary>Runs <paramref name="script"/> as root, after the user types an administrator's password.</summary>
    private async Task RunAsRootAsync(string script, CancellationToken cancellationToken)
    {
        var spec = _system == WakeHelperSystem.Mac
            ? new ProcessStartSpec("/usr/bin/osascript", ["-e", $"do shell script \"{AppleScriptString(script)}\" with administrator privileges"])
            : new ProcessStartSpec("/usr/bin/pkexec", ["/bin/sh", "-c", script]);
        try
        {
            var result = await ProcessRunner.RunAsync(_launcher, spec, InstallTimeout, _time, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _error = result.ExitCode == 0 ? null : $"The administrator's step didn't finish (exit code {result.ExitCode}): {result.StandardError.Trim()}";
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            lock (_lock)
            {
                _error = $"The administrator's step couldn't run: {ex.Message}";
            }
        }
    }

    // ---- What's installed -----------------------------------------------------------------------------------------

    /// <summary>
    /// The helper script. It checks its words before anything else: <c>set</c> with whole seconds within the next 31 days,
    /// or <c>clear</c>; anything else exits 64, and a time out of range 65.
    /// </summary>
    internal string HelperScript() => _system == WakeHelperSystem.Mac ? MacHelper : LinuxHelper;

    private const string Checks = """
        set -eu
        PATH=/usr/bin:/bin:/usr/sbin:/sbin
        export PATH
        when=""
        case "${1:-}" in
          clear) [ "$#" -eq 1 ] || exit 64 ;;
          set)
            [ "$#" -eq 2 ] || exit 64
            case "$2" in ''|*[!0-9]*) exit 64 ;; esac
            [ "${#2}" -le 11 ] || exit 65
            now=$(date +%s)
            [ "$2" -gt "$now" ] || exit 65
            [ "$2" -le $((now + 2678400)) ] || exit 65
            when="$2"
            ;;
          *) exit 64 ;;
        esac

        """;

    private const string MacHelper = "#!/bin/sh\n# Deventry's wake helper (DESIGN.md §18, \"Timed messages\"): one wake for the next timed message, or none.\n"
        + Checks + """
        state=/Library/PrivilegedHelperTools/app.deventry.wake.last
        if [ -f "$state" ]; then
          last=$(cat "$state")
          case "$last" in
            [0-9][0-9]/[0-9][0-9]/[0-9][0-9]\ [0-9][0-9]:[0-9][0-9]:[0-9][0-9]) pmset schedule cancel wake "$last" Deventry >/dev/null 2>&1 || true ;;
          esac
          rm -f "$state"
        fi
        if [ -n "$when" ]; then
          at=$(date -r "$when" "+%m/%d/%y %H:%M:%S")
          pmset schedule wake "$at" Deventry
          printf '%s' "$at" > "$state"
        fi
        """;

    private const string LinuxHelper = "#!/bin/sh\n# Deventry's wake helper (DESIGN.md §18, \"Timed messages\"): one wake for the next timed message, or none.\n"
        + Checks + """
        if [ -n "$when" ]; then
          rtcwake -m no -t "$when" >/dev/null
        else
          rtcwake -m disable >/dev/null
        fi
        """;

    /// <summary>The rule: on macOS a sudoers line for the user and the helper; on Linux a polkit action for the helper.</summary>
    internal string RuleText() => _system == WakeHelperSystem.Mac
        ? $"# Deventry's wake helper (DESIGN.md §18, \"Timed messages\"): {_user} may run it, and nothing else, without a password.\n{_user} ALL=(root) NOPASSWD: {HelperPath}\n"
        : $"""
          <?xml version="1.0" encoding="UTF-8"?>
          <!DOCTYPE policyconfig PUBLIC "-//freedesktop//DTD PolicyKit Policy Configuration 1.0//EN" "http://www.freedesktop.org/standards/PolicyKit/1/policyconfig.dtd">
          <policyconfig>
            <vendor>Deventry</vendor>
            <action id="app.deventry.wake">
              <description>Wake the computer for Deventry's next timed message</description>
              <message>Deventry wants to set when the computer wakes</message>
              <defaults>
                <allow_any>no</allow_any>
                <allow_inactive>no</allow_inactive>
                <allow_active>yes</allow_active>
              </defaults>
              <annotate key="org.freedesktop.policykit.exec.path">{HelperPath}</annotate>
            </action>
          </policyconfig>

          """;

    /// <summary>
    /// What root runs to install: the helper and the rule decoded from the command itself into files only root can
    /// write, and on macOS the rule checked with <c>visudo</c> before it's put in place, since a broken sudoers file can
    /// stop <c>sudo</c> working at all.
    /// </summary>
    internal string InstallScript()
    {
        var helper = Convert.ToBase64String(Encoding.UTF8.GetBytes(HelperScript()));
        var rule = Convert.ToBase64String(Encoding.UTF8.GetBytes(RuleText()));
        var directory = Path.GetDirectoryName(HelperPath)!.Replace('\\', '/');
        var ruleDirectory = Path.GetDirectoryName(RulePath)!.Replace('\\', '/');
        var decode = _system == WakeHelperSystem.Mac ? "/usr/bin/base64 -D" : "/usr/bin/base64 -d";
        var owner = _system == WakeHelperSystem.Mac ? "root:wheel" : "root:root";
        var check = _system == WakeHelperSystem.Mac ? $"/usr/sbin/visudo -cf {RulePath}.new >/dev/null && " : "";
        var ruleMode = _system == WakeHelperSystem.Mac ? "440" : "644";
        return string.Join(" && ",
            "set -e",
            // Folders made before the umask, so Deventry, running as the user, can see what's in them.
            $"mkdir -p {directory} {ruleDirectory}",
            "umask 077",
            $"printf '%s' '{helper}' | {decode} > {HelperPath}.new",
            $"chown {owner} {HelperPath}.new",
            $"chmod 755 {HelperPath}.new",
            $"mv -f {HelperPath}.new {HelperPath}",
            $"printf '%s' '{rule}' | {decode} > {RulePath}.new",
            $"chown {owner} {RulePath}.new",
            $"chmod {ruleMode} {RulePath}.new",
            $"{check}mv -f {RulePath}.new {RulePath}");
    }

    /// <summary>What root runs to remove the helper, its rule and what it kept.</summary>
    internal string UninstallScript() => _system == WakeHelperSystem.Mac
        ? $"rm -f {RulePath} {HelperPath} {MacState}"
        : $"rm -f {RulePath} {HelperPath}";

    /// <summary>A user name a sudoers line can hold as it is: letters, digits, dots, dashes and underscores.</summary>
    internal static bool IsSafeUser(string user) => Regex.IsMatch(user, "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$");

    /// <summary>Text in an AppleScript string: its backslashes and quotes escaped.</summary>
    private static string AppleScriptString(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    public string Describe()
    {
        var setup = Setup switch
        {
            WakeSetup.HelperInstalled => $"The wake helper is installed at {HelperPath}.",
            WakeSetup.NeedsHelper => "The wake helper isn't installed.",
            _ => UnavailableReason ?? "",
        };
        lock (_lock)
        {
            var wake = _at is { } at ? $" A wake is set for {at.UtcDateTime:yyyy-MM-dd HH:mm} UTC." : " No wake is set.";
            return _error is { } error ? $"{setup}{wake} {error}" : $"{setup}{wake}";
        }
    }

    /// <summary>Deventry is closing: its wake is cleared, since a message isn't sent while it's closed.</summary>
    public void Dispose()
    {
        if (WakesAt is not null)
        {
            // Quick, and nothing waits on Deventry's exit for it but this.
            _ = WakeAtAsync(null).Wait(TimeSpan.FromSeconds(5));
        }
        lock (_lock)
        {
            _disposed = true;
        }
    }
}

/// <summary>Which system's wake helper.</summary>
public enum WakeHelperSystem
{
    Mac,
    Linux,
}
