namespace Claudette.App.ViewModels;

/// <summary>
/// Games with Claudette (DESIGN.md §18): every tab's Games page follows the one game that's open, and the line over the
/// board says when Claude needs the user.
/// </summary>
public sealed partial class ShellViewModel
{
    private void WatchGames()
    {
        _services.Games.Announcer = Announce;
        _services.Games.OpenChanged += () =>
        {
            foreach (var tab in AllTabs)
            {
                tab.OnGameOpenChanged();
            }
        };
    }

    /// <summary>
    /// The line over the board while tabs wait on the user: the other tabs first, with <b>Go there</b>; the one in front
    /// needs nothing to go to.
    /// </summary>
    private void UpdateGameCall()
    {
        var waiting = AllTabs.Where(t => t.NeedsInput).ToList();
        var others = waiting.Where(t => !ReferenceEquals(t, SelectedTab)).ToList();
        if (others is [var only])
        {
            _services.Games.SetCall($"Claude needs you in {only.DisplayName}", () => SelectTab(only.Id));
        }
        else if (others.Count > 1)
        {
            _services.Games.SetCall($"Claude needs you in {others.Count} tabs", SelectNextNeedingInput);
        }
        else
        {
            _services.Games.SetCall(waiting.Count > 0 ? "Claude is waiting for you in this tab" : null, null);
        }
    }
}
