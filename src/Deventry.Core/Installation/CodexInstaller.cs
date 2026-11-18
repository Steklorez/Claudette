using System.Text.RegularExpressions;
using Deventry.Core.Claude;
using Deventry.Core.Diffs;
using Deventry.Core.Processes;
using Deventry.Core.ProjectTools;

namespace Deventry.Core.Installation;

public sealed record CodexInstallResult(bool Succeeded, string Message);

public interface ICodexInstaller
{
    /// <summary>The command, as the setup screen and Settings show it before it runs.</summary>
    string CommandText { get; }

    Task<CodexInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Installs Codex with the command its docs give (DESIGN.md §18, "Codex, a second agent", "Installing and signing in"):
/// <c>irm https://chatgpt.com/codex/install.ps1 | iex</c> in PowerShell on Windows, <c>curl -fsSL
/// https://chatgpt.com/codex/install.sh | sh</c> elsewhere, into the user's home with no admin rights, as
/// <see cref="ClaudeInstaller"/> installs Claude Code. <c>CODEX_NON_INTERACTIVE</c> has its prompts take their defaults.
/// </summary>
public sealed partial class CodexInstaller(
    ToolOS os,
    string workingDirectory,
    IProcessLauncher launcher,
    TimeProvider timeProvider,
    IFileProbe? probe = null,
    UserEnvironment? userEnvironment = null) : ICodexInstaller
{
    public const string WindowsCommand = "irm https://chatgpt.com/codex/install.ps1 | iex";

    public const string UnixCommand = "curl -fsSL https://chatgpt.com/codex/install.sh | sh";

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);

    private readonly IFileProbe _probe = probe ?? FileProbe.Instance;

    public string CommandText => os == ToolOS.Windows ? WindowsCommand : UnixCommand;

    public ProcessStartSpec Command => os == ToolOS.Windows
        ? new ProcessStartSpec(PowerShell(), ["-NoProfile", "-NonInteractive", "-Command", $"[Console]::OutputEncoding = [Text.Encoding]::UTF8; {WindowsCommand}"])
        : new ProcessStartSpec(_probe.FindOnPath("bash") ?? "/bin/bash", ["-c", $"set -o pipefail; {UnixCommand}"]);

    public async Task<CodexInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var baseEnvironment = userEnvironment is null ? null : await userEnvironment.GetAsync(cancellationToken).ConfigureAwait(false);
            var spec = Command with
            {
                WorkingDirectory = workingDirectory,
                Environment = ClaudeEnvironment.From(baseEnvironment, new Dictionary<string, string?> { ["CODEX_NON_INTERACTIVE"] = "1" }),
            };
            result = await ProcessRunner.RunAsync(launcher, spec, InstallTimeout, timeProvider, cancellationToken,
                onOutput is null ? null : line => onOutput(Plain(line))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new CodexInstallResult(false, $"Couldn't run '{CommandText}': {ex.Message}");
        }
        if (result.ExitCode != 0)
        {
            var lines = Plain(result.StandardError + "\n" + result.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new CodexInstallResult(false, $"'{CommandText}' failed (exit code {result.ExitCode}). {string.Join(" ", lines.TakeLast(3))}".Trim());
        }
        return new CodexInstallResult(true, "Codex is installed.");
    }

    private string PowerShell() =>
        _probe.FindOnPath("powershell.exe")
        ?? (_probe.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe") is var known && !known.Contains('%', StringComparison.Ordinal) && _probe.FileExists(known)
            ? known
            : "powershell.exe");

    private static string Plain(string line) => AnsiEscape().Replace(line, "");

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
