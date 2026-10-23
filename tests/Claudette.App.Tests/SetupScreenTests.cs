using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Installation;

namespace Claudette.App.Tests;

/// <summary>
/// The setup screen's Install Claude Code, and what it says about the Claude desktop app (DESIGN.md §12, "Installing
/// Claude Code"). The installer is always a fake.
/// </summary>
public class SetupScreenTests
{
    private static readonly ClaudeLocateResult Missing = new(null, ClaudeInstallProblem.NotFound, "Claude Code wasn't found.");

    [Fact]
    public async Task Install_shows_the_installers_output_then_looks_for_Claude_Code_again()
    {
        var checks = new List<string?>();
        var installer = new FakeClaudeInstaller();
        var setup = Setup(Missing, installer, checks);

        Assert.True(setup.CanInstall);
        Assert.Equal("irm https://claude.ai/install.ps1 | iex", setup.InstallCommandText);
        Assert.False(setup.ChecksFirst);

        await setup.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, installer.Installs);
        Assert.Contains("✅ Installation complete!", setup.Output);
        Assert.Equal("Claude Code is installed.", setup.ResultText);
        Assert.False(setup.IsRunning);
        // Looks for it, as Check again does, rather than at a path.
        Assert.Equal([null], checks);
    }

    [Fact]
    public async Task A_failed_install_says_why_and_stays_on_the_screen()
    {
        var checks = new List<string?>();
        var setup = Setup(Missing, new FakeClaudeInstaller { Fails = true }, checks);

        await setup.InstallCommand.ExecuteAsync(null);

        Assert.Equal("'irm https://claude.ai/install.ps1 | iex' failed (exit 1). Failed to get latest version", setup.ResultText);
        Assert.Empty(checks);
        Assert.True(setup.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task While_it_installs_the_screen_says_so_and_nothing_else_can_run()
    {
        var finish = new TaskCompletionSource<ClaudeInstallResult>();
        var setup = new SetupViewModel(Missing, new NoPlatform(), _ => Task.CompletedTask,
            install: new SetupInstall(ClaudeInstaller.UnixCommand, _ => finish.Task));

        var installing = setup.InstallCommand.ExecuteAsync(null);

        Assert.True(setup.IsInstalling);
        Assert.False(setup.InstallCommand.CanExecute(null));

        finish.SetResult(new ClaudeInstallResult(false, "No."));
        await installing;

        Assert.False(setup.IsInstalling);
    }

    [Fact]
    public void The_desktop_app_is_explained_when_Claude_Code_is_missing()
    {
        var setup = Setup(Missing with { DesktopInstalled = true }, new FakeClaudeInstaller());

        Assert.Equal("Claude Code isn't installed", setup.Title);
        Assert.True(setup.ShowsDesktopNote);
        Assert.False(Setup(Missing, new FakeClaudeInstaller()).ShowsDesktopNote);
    }

    [Fact]
    public async Task The_desktop_apps_copy_of_Claude_Code_isnt_used_and_installing_is_offered()
    {
        await using var h = new TabTestHarness();
        var copy = DesktopCopy(h);
        h.Services.Settings.ClaudeCode.ClaudePath = copy;
        var main = new MainWindowViewModel(h.Services);

        await main.StartAsync();

        var setup = Assert.IsType<SetupViewModel>(main.CurrentPage);
        Assert.Equal("Claude Code isn't installed", setup.Title);
        Assert.StartsWith("That's the copy of Claude Code the Claude desktop app keeps for its own use.", setup.Detail);
        Assert.Equal(copy, setup.FoundPath);
        // The detail already says it's the desktop app's.
        Assert.False(setup.ShowsDesktopNote);
        Assert.True(setup.CanInstall);
        Assert.Equal(h.Installer.CommandText, setup.InstallCommandText);
        Assert.Equal(0, h.Installer.Installs);
    }

    [Fact]
    public void Install_isnt_offered_for_a_version_that_is_too_old_or_one_that_doesnt_answer()
    {
        // Update now is, for too old (ClaudeUpdateTests); Check again is the main action otherwise.
        var setup = new SetupViewModel(new ClaudeLocateResult(null, ClaudeInstallProblem.VersionUnreadable, "No version"), new NoPlatform(), _ => Task.CompletedTask);

        Assert.False(setup.CanInstall);
        Assert.True(setup.ChecksFirst);
    }

    private static SetupViewModel Setup(ClaudeLocateResult result, FakeClaudeInstaller installer, List<string?>? checks = null) =>
        new(result, new NoPlatform(), path =>
        {
            checks?.Add(path);
            return Task.CompletedTask;
        }, install: new SetupInstall(installer.CommandText, output => installer.InstallAsync(output)));

    /// <summary>Where the desktop app keeps its copy of Claude Code, in the harness's folders for other programs; it needn't exist.</summary>
    private static string DesktopCopy(TabTestHarness h) =>
        OperatingSystem.IsWindows() ? Path.Combine(h.Root, "localappdata", "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.308", "c17a819afb40", "claude.exe")
        : OperatingSystem.IsMacOS() ? Path.Combine(h.Root, "home", "Library", "Application Support", "Claude", "claude-code", "2.1.308", "claude")
        : Path.Combine(h.Root, "home", ".config", "Claude", "claude-code", "2.1.308", "claude");
}
