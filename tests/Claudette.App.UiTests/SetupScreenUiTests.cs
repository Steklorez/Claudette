using Avalonia.Headless.XUnit;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.Core.Installation;

namespace Claudette.App.UiTests;

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
}
