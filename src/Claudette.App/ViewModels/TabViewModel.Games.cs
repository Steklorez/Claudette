using Claudette.App.Games;
using Claudette.App.Services;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Games with Claudette (DESIGN.md §18): the side panel's Games page, there while a game is open, and starting one from
/// her menu, the command palette or the usage limit bar. The game is the app's, not the tab's: every tab shows the same.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The game open, shared by the tabs.</summary>
    public GameService Games => _services.Games;

    /// <summary>The Games page: the game being played against Claudette.</summary>
    public bool IsGamesPage
    {
        get => Page == SidePanelPage.Games;
        set => SetPage(SidePanelPage.Games, value);
    }

    /// <summary>The game for the page, while the page shows one; null otherwise, so the page isn't made until it shows.</summary>
    public GameService? ShownGames => IsGamesPage && Games.IsOpen ? Games : null;

    /// <summary>The usage limit bar's <b>Play a game while you wait</b>: until the limit resets, whether or not the task will continue.</summary>
    public bool CanPlayWhileWaiting => Games.IsEnabled && LimitWait is { HasReset: false };

    [RelayCommand]
    private void ShowGamesPage() => Page = SidePanelPage.Games;

    /// <summary>Starts <paramref name="kind"/> against Claudette, or goes back to it, on this tab's Games page.</summary>
    [RelayCommand]
    public void PlayGame(GameKind kind)
    {
        if (Games.Start(kind) is not null)
        {
            OpenSidePanelPage(SidePanelPage.Games);
        }
    }

    /// <summary>A game opened or closed: the page comes, or goes with the game.</summary>
    internal void OnGameOpenChanged()
    {
        if (!Games.IsOpen && IsGamesPage)
        {
            Page = SidePanelPage.Files;
        }
        OnPropertyChanged(nameof(ShownGames));
    }

    private void OnGamesSettingsChanged() => OnPropertyChanged(nameof(CanPlayWhileWaiting));
}
