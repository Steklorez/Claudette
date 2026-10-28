using Claudette.App.Games;
using Claudette.App.Services;
using Claudette.App.ViewModels.ToolWindows;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Games with Claudette (DESIGN.md §18): starting one from her menu, the command palette or the usage limit bar, which
/// shows the Games tool window. The game is the app's, not the tab's: the window shows the same whichever tab has the focus.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The game open, shared by the tabs.</summary>
    public GameService Games => _services.Games;

    /// <summary>The usage limit bar's <b>Play a game while you wait</b>: until the limit resets, whether or not the task will continue.</summary>
    public bool CanPlayWhileWaiting => Games.IsEnabled && LimitWait is { HasReset: false };

    /// <summary>Starts <paramref name="kind"/> against Claudette, or goes back to it, in the Games tool window.</summary>
    [RelayCommand]
    public void PlayGame(GameKind kind)
    {
        if (Games.Start(kind) is not null)
        {
            ShowToolWindow(ToolWindowKinds.Games);
        }
    }

    private void OnGamesSettingsChanged() => OnPropertyChanged(nameof(CanPlayWhileWaiting));
}
