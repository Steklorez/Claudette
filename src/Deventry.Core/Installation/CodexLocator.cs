using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Deventry.Core.Claude;
using Deventry.Core.Processes;

namespace Deventry.Core.Installation;

public sealed record CodexInstall(string Path, Version Version);

public enum CodexInstallProblem
{
    NotFound,
    VersionUnreadable,
    TooOld,
}

public sealed record CodexLocateResult(CodexInstall? Install, CodexInstallProblem? Problem, string? Detail, string? FoundPath = null)
{
    public bool IsUsable => Install is not null && Problem is null;
}

/// <summary>
/// Finds the <c>codex</c> to run (DESIGN.md §18, "Codex, a second agent"), as <see cref="ClaudeLocator"/> finds
/// <c>claude</c>: the one Settings names, else one on the <c>PATH</c> or where Codex's installers put it, checked by its
/// <c>--version</c>.
/// </summary>
public sealed partial class CodexLocator(IProcessLauncher launcher, TimeProvider timeProvider, UserEnvironment? environment = null)
{
    /// <summary>The oldest Codex Deventry supports. Kept in step with compat/codex-surface.yaml.</summary>
    public static readonly Version MinimumVersion = new(0, 169, 0);

    /// <summary>The newest Codex Deventry has been checked against. Kept in step with compat/codex-surface.yaml.</summary>
    public static readonly Version LastTestedVersion = new(0, 169, 0);

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    public async Task<CodexLocateResult> LocateAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        var userEnvironment = environment is null ? null : await environment.GetAsync(cancellationToken).ConfigureAwait(false);
        var searchPath = userEnvironment?.GetValueOrDefault("PATH") ?? Environment.GetEnvironmentVariable("PATH");
        var path = string.IsNullOrWhiteSpace(configuredPath) ? FindOnSystem(searchPath) : configuredPath;
        if (path is null)
        {
            return new CodexLocateResult(null, CodexInstallProblem.NotFound, "Codex wasn't found.");
        }
        if (!File.Exists(path))
        {
            return new CodexLocateResult(null, CodexInstallProblem.NotFound, $"'{path}' doesn't exist.", path);
        }
        ProcessResult result;
        try
        {
            var spec = new ProcessStartSpec(path, ["--version"]) { Environment = ClaudeEnvironment.From(userEnvironment) };
            result = await ProcessRunner.RunAsync(launcher, spec, VersionTimeout, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CodexLocateResult(null, CodexInstallProblem.VersionUnreadable, $"Running '{path} --version' failed: {ex.Message}", path);
        }
        if (!TryParseVersion(result.StandardOutput, out var version))
        {
            return new CodexLocateResult(null, CodexInstallProblem.VersionUnreadable,
                result.StandardOutput.Trim() is { Length: > 0 } output ? $"Couldn't read the version from '{output}'." : $"'{path} --version' printed no version.", path);
        }
        var install = new CodexInstall(path, version);
        return version < MinimumVersion
            ? new CodexLocateResult(install, CodexInstallProblem.TooOld, $"Codex {version} is installed. Deventry needs {MinimumVersion} or later.", path)
            : new CodexLocateResult(install, null, null, path);
    }

    /// <summary>Reads a version like <c>codex-cli 0.169.0</c>.</summary>
    public static bool TryParseVersion(string output, out Version version)
    {
        var match = VersionPattern().Match(output);
        if (match.Success && Version.TryParse(match.Value, out var parsed))
        {
            version = parsed;
            return true;
        }
        version = new Version();
        return false;
    }

    /// <summary>
    /// On <paramref name="searchPath"/>, then where Codex's installers put it: its own installer's folder (and
    /// <c>~/.local/bin</c> elsewhere), Homebrew, and the native program inside npm's package, since npm's <c>codex</c>
    /// launcher on Windows is a script that can't be started without a shell.
    /// </summary>
    private static string? FindOnSystem(string? searchPath)
    {
        var name = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var (package, triple) = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? ("codex-win32-arm64", "aarch64-pc-windows-msvc")
            : ("codex-win32-x64", "x86_64-pc-windows-msvc");
        string[] known = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
                Path.Combine(home, ".local", "bin", "codex.exe"),
                Path.Combine(roaming, "npm", "node_modules", "@openai", package, "vendor", triple, "bin", "codex.exe"),
                Path.Combine(roaming, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", package, "vendor", triple, "bin", "codex.exe"),
            ]
            : [Path.Combine(home, ".local", "bin", "codex"), "/opt/homebrew/bin/codex", "/usr/local/bin/codex", "/usr/bin/codex"];
        return OnPath(searchPath, name).Concat(known).FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> OnPath(string? searchPath, string name)
    {
        foreach (var directory in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim().Trim('"'), name);
            }
            catch (ArgumentException)
            {
                continue;
            }
            yield return candidate;
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}
