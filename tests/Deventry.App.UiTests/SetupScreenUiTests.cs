using Avalonia.Headless.XUnit;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;
using Deventry.Core.Installation;

namespace Deventry.App.UiTests;

/// <summary>
/// The setup screen rendered when Claude Code is missing and the Claude desktop app is installed (DESIGN.md §12,
/// "Installing Claude Code"): the note about the desktop app, the installer's command and Install Claude Code first.
/// </summary>
public class SetupScreenUiTests
{
    [AvaloniaFact]
    public async Task Missing_with_the_desktop_app_installed_explains_it_and_offers_the_installer()
    {
        var installer = new FakeClaudeInstaller();
        var setup = new SetupViewModel(
            new ClaudeLocateResult(null, ClaudeInstallProblem.NotFound, "Claude Code wasn't found.", DesktopInstalled: true),
            new NoPlatform(),
            _ => Task.CompletedTask,
            install: new SetupInstall(installer.CommandText, output => installer.InstallAsync(output)));
        var window = UiText.Show(new SetupView { DataContext = setup }, 900, 600);

        await setup.InstallCommand.ExecuteAsync(null);
        UiText.Settle(window);

        await Verify(UiText.Describe(window));
    }

    [AvaloniaFact]
    public async Task Codex_is_offered_beside_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.Services.LocateCodexAsync();
        using var codex = new CodexSetupViewModel(h.Services);
        var setup = new SetupViewModel(new ClaudeLocateResult(null, ClaudeInstallProblem.NotFound, "Claude Code wasn't found."), new NoPlatform(), _ => Task.CompletedTask,
            codex: codex);
        var window = UiText.Show(new SetupView { DataContext = setup }, 900, 700);
        UiText.Settle(window);

        await Verify(UiText.Describe(window));
    }
}
