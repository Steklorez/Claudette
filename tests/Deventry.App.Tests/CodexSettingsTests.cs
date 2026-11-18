using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Codex;
using Deventry.Core.Installation;

namespace Deventry.App.Tests;

/// <summary>Settings → Agents → Codex (DESIGN.md §18, "Codex, a second agent", "Installing and signing in").</summary>
public sealed class CodexSettingsTests
{
    [Fact]
    public async Task Without_codex_it_offers_to_install_it_and_finds_it_once_it_has()
    {
        await using var h = new TabTestHarness();
        var installed = false;
        h.Services.FindCodex = _ => Task.FromResult(installed
            ? new CodexLocateResult(new CodexInstall(Path.Combine(h.Root, "bin", "codex"), new Version(0, 169, 0)), null, null)
            : new CodexLocateResult(null, CodexInstallProblem.NotFound, "Codex wasn't found."));
        h.CodexInstaller.OnInstall = () => installed = true;
        await h.Services.LocateCodexAsync();
        using var settings = new SettingsViewModel(h.Services, null) { SelectedCategory = SettingsCategory.Codex };
        var page = settings.Codex.Setup;

        Assert.Same(settings.Codex, settings.CurrentPage);
        Assert.Equal("Codex wasn't found.", page.FoundText);
        Assert.True(page.CanInstall);
        Assert.False(page.CanSignIn);
        Assert.Equal("Codex isn't found yet.", page.SignInText);

        await page.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, h.CodexInstaller.Installs);
        Assert.Equal("Codex is installed.", page.InstallOutput);
        await TabTestHarness.Eventually(() => page.IsFound, "Codex to be found");
        Assert.Equal($"Codex 0.169.0, at {Path.Combine(h.Root, "bin", "codex")}", page.FoundText);
        Assert.False(page.CanInstall);
    }

    [Fact]
    public async Task The_setup_screen_offers_codex_beside_claude_code()
    {
        await using var h = new TabTestHarness();
        h.Services.Settings.ClaudeCode.ClaudePath = Path.Combine(h.Root, "missing", "claude");
        var installed = false;
        h.Services.FindCodex = _ => Task.FromResult(installed
            ? new CodexLocateResult(new CodexInstall(Path.Combine(h.Root, "bin", "codex"), new Version(0, 169, 0)), null, null)
            : new CodexLocateResult(null, CodexInstallProblem.NotFound, "Codex wasn't found."));
        h.CodexInstaller.OnInstall = () => installed = true;
        var main = new MainWindowViewModel(h.Services);

        await main.StartAsync();

        var codex = Assert.IsType<SetupViewModel>(main.CurrentPage).Codex!;
        await TabTestHarness.Eventually(() => codex.Summary == "Codex isn't installed.", "Codex to be looked for");
        Assert.True(codex.CanInstall);
        await codex.InstallCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => codex.IsFound, "Codex to be found");
        // The real codex never runs in tests, so it can't say it's signed in.
        await TabTestHarness.Eventually(() => codex.Summary == "Codex is installed. Sign in to use it.", "Codex's sign-in to be asked");
        Assert.True(codex.CanSignIn);
    }

    [Fact]
    public async Task A_failed_install_says_why_and_offers_it_again()
    {
        await using var h = new TabTestHarness();
        h.CodexInstaller.Fails = true;
        await h.Services.LocateCodexAsync();
        using var settings = new SettingsViewModel(h.Services, null) { SelectedCategory = SettingsCategory.Codex };

        await settings.Codex.Setup.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("'irm https://chatgpt.com/codex/install.ps1 | iex' failed", settings.Codex.Setup.InstallOutput);
        Assert.True(settings.Codex.Setup.CanInstall);
    }

    [Fact]
    public async Task New_codex_tabs_start_in_the_mode_and_model_it_says()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null) { SelectedCategory = SettingsCategory.Codex };

        settings.Codex.DefaultMode = settings.Codex.ModeChoices.Single(c => c.Value == CodexModes.ReadOnly);
        settings.Codex.DefaultModel = " gpt-fake-1 ";

        Assert.Equal((CodexModes.ReadOnly, "gpt-fake-1"), (h.Services.Settings.Codex.DefaultMode, h.Services.Settings.Codex.DefaultModel));
        await InlineDispatcher.RunAsync(() => h.Shell.OpenFolderAsync(h.WorkFolder, agent: Core.Agents.CodexAgent.Instance));
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle, "the Codex tab to start");
        var start = Assert.Single(h.CodexFactory.Launches).Start;
        Assert.Equal((CodexModes.ReadOnly, "gpt-fake-1"), (start.Mode, start.Model));
    }
}
