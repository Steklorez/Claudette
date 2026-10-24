using System.Text.RegularExpressions;
using Claudette.Core.Claude;
using Claudette.Core.Diffs;
using Claudette.Core.Processes;
using Claudette.Core.ProjectTools;

namespace Claudette.Core.Installation;

/// <summary>How <b>Install Claude Code</b> went.</summary>
public sealed record ClaudeInstallResult(bool Succeeded, string Message);

/// <summary>Installs Claude Code. An interface so the app's tests can fake it.</summary>
public interface IClaudeInstaller
{
    /// <summary>The command as Claude Code's setup page gives it, which the setup screen shows before running it.</summary>
    string CommandText { get; }

    /// <param name="onOutput">Each line the installer prints, for the setup screen. Called on a background thread.</param>
    Task<ClaudeInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Claude Code's native installer, with the command its setup page gives for this OS (DESIGN.md §12, "Installing Claude
/// Code"): <c>irm https://claude.ai/install.ps1 | iex</c> in PowerShell on Windows, and
/// <c>curl -fsSL https://claude.ai/install.sh | bash</c> on macOS and Linux. It installs into the user's home with no
/// admin rights, to <c>~/.local/bin</c>, where <see cref="ClaudeLocator"/> looks. Run through <see cref="IProcessLauncher"/>.
/// </summary>
/// <param name="workingDirectory">Where it runs: a neutral folder of Claudette's, as for the update commands.</param>
/// <param name="probe">Finds PowerShell or bash on the user's <c>PATH</c>.</param>
/// <param name="environmentOverrides">Applied on top of the cleaned environment (DESIGN.md §13): the installer runs <c>claude install</c>.</param>
/// <param name="userEnvironment">The user environment the installer starts from (DESIGN.md §13). Null: Claudette's own.</param>
public sealed partial class ClaudeInstaller(
    ToolOS os,
    string workingDirectory,
    IProcessLauncher launcher,
    TimeProvider timeProvider,
    IFileProbe? probe = null,
    IReadOnlyDictionary<string, string?>? environmentOverrides = null,
    UserEnvironment? userEnvironment = null)
    : IClaudeInstaller
{
    public const string WindowsCommand = "irm https://claude.ai/install.ps1 | iex";

    public const string UnixCommand = "curl -fsSL https://claude.ai/install.sh | bash";

    /// <summary>The installer downloads a few hundred megabytes and prints nothing meanwhile.</summary>
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);

    private readonly IFileProbe _probe = probe ?? FileProbe.Instance;

    public string CommandText => os == ToolOS.Windows ? WindowsCommand : UnixCommand;

    /// <summary>
    /// What runs: the command in PowerShell or bash. PowerShell writes UTF-8, for the installer's ✅. In bash, a failed
    /// <c>curl</c> fails the whole command, rather than piping nothing into a <c>bash</c> that succeeds.
    /// </summary>
    public ProcessStartSpec Command => os == ToolOS.Windows
        ? new ProcessStartSpec(PowerShell(), ["-NoProfile", "-NonInteractive", "-Command", $"[Console]::OutputEncoding = [Text.Encoding]::UTF8; {WindowsCommand}"])
        : new ProcessStartSpec(_probe.FindOnPath("bash") ?? "/bin/bash", ["-c", $"set -o pipefail; {UnixCommand}"]);

    public async Task<ClaudeInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var baseEnvironment = userEnvironment is null ? null : await userEnvironment.GetAsync(cancellationToken).ConfigureAwait(false);
            var spec = Command with { WorkingDirectory = workingDirectory, Environment = ClaudeEnvironment.From(baseEnvironment, environmentOverrides) };
            result = await ProcessRunner.RunAsync(launcher, spec, InstallTimeout, timeProvider, cancellationToken,
                onOutput is null ? null : line => onOutput(Plain(line))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ClaudeInstallResult(false, $"Couldn't run '{CommandText}': {ex.Message}");
        }
        if (result.ExitCode != 0)
        {
            var detail = LastLines(result.StandardError) ?? LastLines(result.StandardOutput) ?? "";
            return new ClaudeInstallResult(false, $"'{CommandText}' failed (exit code {result.ExitCode}). {detail}".Trim());
        }
        return new ClaudeInstallResult(true, "Claude Code is installed.");
    }

    private string PowerShell() =>
        _probe.FindOnPath("powershell.exe")
        ?? (_probe.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe") is var known && !known.Contains('%', StringComparison.Ordinal) && _probe.FileExists(known)
            ? known
            : "powershell.exe");

    private static string Plain(string line) => AnsiEscape().Replace(line, "");

    private static string? LastLines(string text)
    {
        var lines = Plain(text).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : string.Join(" ", lines.TakeLast(3));
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
