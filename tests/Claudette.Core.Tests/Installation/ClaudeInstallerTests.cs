using Claudette.Core.Diffs;
using Claudette.Core.Installation;
using Claudette.Core.Processes;
using Claudette.Core.ProjectTools;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.Installation;

/// <summary>
/// Claude Code's native installer, run from the setup screen (DESIGN.md §12, "Installing Claude Code"): the command for
/// each OS, and how a run is reported. Nothing here runs the real installer.
/// </summary>
public sealed class ClaudeInstallerTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-installer");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Windows_runs_the_PowerShell_command_from_Claude_Codes_setup_page()
    {
        var installer = Installer(ToolOS.Windows, new FakeProcessLauncher(), new Probe { ["powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" });

        Assert.Equal("irm https://claude.ai/install.ps1 | iex", installer.CommandText);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", installer.Command.FileName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], installer.Command.Arguments.Take(3));
        Assert.EndsWith("; irm https://claude.ai/install.ps1 | iex", installer.Command.Arguments[3]);
    }

    [Theory]
    [InlineData(ToolOS.MacOS)]
    [InlineData(ToolOS.Linux)]
    public void macOS_and_Linux_run_the_bash_command_from_Claude_Codes_setup_page(ToolOS os)
    {
        var installer = Installer(os, new FakeProcessLauncher(), new Probe());

        Assert.Equal("curl -fsSL https://claude.ai/install.sh | bash", installer.CommandText);
        Assert.Equal("/bin/bash", installer.Command.FileName);
        // A curl that fails fails the command, rather than piping nothing into a bash that succeeds.
        Assert.Equal(["-c", "set -o pipefail; curl -fsSL https://claude.ai/install.sh | bash"], installer.Command.Arguments);
    }

    [Fact]
    public async Task A_run_passes_on_its_output_and_succeeds()
    {
        var launcher = new FakeProcessLauncher { Respond = _ => new ProcessResult(0, "Setting up Claude Code...\n\x1B[32m✔\x1B[0m Claude Code successfully installed!\n✅ Installation complete!\n", "") };
        var lines = new List<string>();

        var result = await Installer(ToolOS.Linux, launcher, new Probe { ["bash"] = "/usr/bin/bash" })
            .InstallAsync(line => { lock (lines) { lines.Add(line); } }, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Claude Code is installed.", result.Message);
        Assert.Contains("✔ Claude Code successfully installed!", lines);
        var started = Assert.Single(launcher.Started);
        Assert.Equal("/usr/bin/bash", started.FileName);
        Assert.Equal(_temp.Combine("utility"), started.WorkingDirectory);
        Assert.True(Directory.Exists(_temp.Combine("utility")));
        // Cleaned as every claude's is (DESIGN.md §13): the installer ends by running claude install.
        Assert.NotNull(started.Environment);
    }

    [Fact]
    public async Task A_failed_run_says_why()
    {
        var launcher = new FakeProcessLauncher
        {
            Respond = _ => new ProcessResult(1, "", "Failed to get a valid version from downloads.claude.ai (got unexpected content).\nThis can happen if the download service is unreachable or not available in your region\n"),
        };

        var result = await Installer(ToolOS.Windows, launcher, new Probe()).InstallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.StartsWith("'irm https://claude.ai/install.ps1 | iex' failed (exit 1). Failed to get a valid version", result.Message);
    }

    [Fact]
    public async Task A_shell_that_wont_start_is_reported()
    {
        var launcher = new FakeProcessLauncher { StartFailure = new System.ComponentModel.Win32Exception("The system cannot find the file specified.") };

        var result = await Installer(ToolOS.Windows, launcher, new Probe()).InstallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Couldn't run 'irm https://claude.ai/install.ps1 | iex': The system cannot find the file specified.", result.Message);
    }

    private ClaudeInstaller Installer(ToolOS os, FakeProcessLauncher launcher, Probe probe) =>
        new(os, _temp.Combine("utility"), launcher, TimeProvider.System, probe);

    /// <summary>Programs on the user's PATH, by name; no other files.</summary>
    private sealed class Probe : Dictionary<string, string>, IFileProbe
    {
        public bool FileExists(string path) => ContainsValue(path);

        public string? FindOnPath(string fileName) => TryGetValue(fileName, out var path) ? path : null;

        public string ExpandEnvironmentVariables(string path) => path;
    }
}
