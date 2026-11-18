using Deventry.Core.Diffs;
using Deventry.Core.Installation;
using Deventry.Core.Processes;
using Deventry.Core.ProjectTools;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Installation;

/// <summary>
/// Codex's installer, run from Settings → Agents → Codex (DESIGN.md §18, "Installing and signing in"): the command for
/// each OS, and how a run is reported. Nothing here runs the real installer.
/// </summary>
public sealed class CodexInstallerTests : IDisposable
{
    private readonly TempFolder _temp = new("deventry-codex-installer");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Windows_runs_the_PowerShell_command_from_Codexs_docs()
    {
        var installer = Installer(ToolOS.Windows, new FakeProcessLauncher(), new Probe { ["powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" });

        Assert.Equal("irm https://chatgpt.com/codex/install.ps1 | iex", installer.CommandText);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", installer.Command.FileName);
        Assert.EndsWith("; irm https://chatgpt.com/codex/install.ps1 | iex", installer.Command.Arguments[3]);
    }

    [Theory]
    [InlineData(ToolOS.MacOS)]
    [InlineData(ToolOS.Linux)]
    public void macOS_and_Linux_run_the_shell_command_from_Codexs_docs(ToolOS os)
    {
        var installer = Installer(os, new FakeProcessLauncher(), new Probe());

        Assert.Equal("curl -fsSL https://chatgpt.com/codex/install.sh | sh", installer.CommandText);
        Assert.Equal(["-c", "set -o pipefail; curl -fsSL https://chatgpt.com/codex/install.sh | sh"], installer.Command.Arguments);
    }

    [Fact]
    public async Task A_run_asks_nothing_and_succeeds()
    {
        var launcher = new FakeProcessLauncher { Respond = _ => new ProcessResult(0, "Installing Codex...\n\x1B[32mInstalled\x1B[0m codex 0.169.0\n", "") };
        var lines = new List<string>();

        var result = await Installer(ToolOS.Linux, launcher, new Probe { ["bash"] = "/usr/bin/bash" })
            .InstallAsync(line => { lock (lines) { lines.Add(line); } }, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Codex is installed.", result.Message);
        Assert.Contains("Installed codex 0.169.0", lines);
        var started = Assert.Single(launcher.Started);
        Assert.Equal("1", started.Environment!["CODEX_NON_INTERACTIVE"]);
    }

    [Fact]
    public async Task A_failed_run_says_why()
    {
        var launcher = new FakeProcessLauncher { Respond = _ => new ProcessResult(1, "", "Couldn't download codex: connection refused\n") };

        var result = await Installer(ToolOS.Windows, launcher, new Probe()).InstallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("'irm https://chatgpt.com/codex/install.ps1 | iex' failed (exit code 1). Couldn't download codex: connection refused", result.Message);
    }

    private CodexInstaller Installer(ToolOS os, FakeProcessLauncher launcher, Probe probe) =>
        new(os, _temp.Combine("utility"), launcher, TimeProvider.System, probe);

    /// <summary>Programs on the user's PATH, by name; no other files.</summary>
    private sealed class Probe : Dictionary<string, string>, IFileProbe
    {
        public bool FileExists(string path) => ContainsValue(path);

        public string? FindOnPath(string fileName) => TryGetValue(fileName, out var path) ? path : null;

        public string ExpandEnvironmentVariables(string path) => path;
    }
}
