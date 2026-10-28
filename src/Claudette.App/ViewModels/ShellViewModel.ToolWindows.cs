using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.ViewModels;

/// <summary>
/// The tool windows (DESIGN.md §3, "Tool windows"): one dock for the whole window, outside the panes. The windows that
/// follow the session show the focused session tab's data, or with a Project Files tab or a log focused, its group's last
/// focused session tab's; the tabs hear which windows show them, and the sidebar leaves the right edge its room.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The tool windows' dock.</summary>
    public ToolDockViewModel ToolWindows { get; }

    /// <summary>Each group's session tab focused last, by the group's folder: what its Files tab's and logs' windows show.</summary>
    private readonly Dictionary<string, TabViewModel> _lastSessionTab = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The session tab the windows show, which hears which of them do.</summary>
    private TabViewModel? _toolSession;

    private void WatchToolWindows()
    {
        ToolWindows.ShowingChanged += TellTabsWhatShows;
        ToolWindows.Removed += OnToolWindowRemoved;
        ToolWindows.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolDockViewModel.RightRoom))
            {
                Layout.RightEdgeRoom = ToolWindows.RightRoom;
            }
        };
        Layout.RightEdgeRoom = ToolWindows.RightRoom;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Selected) or nameof(IsSplit))
            {
                FollowFocusedTab();
            }
        };
        // The game is the app's: its window shows whichever tab has the focus, while one is open.
        var games = ToolWindows.For(ToolWindowKinds.Games);
        games.Content = _services.Games;
        games.IsAvailable = _services.Games.IsOpen;
    }

    /// <summary>
    /// The focused tab changed: the windows that follow the session show its data, or its group's last focused session
    /// tab's, named over them while that might not be the one the user is looking at.
    /// </summary>
    private void FollowFocusedTab()
    {
        var group = Selected is { } selected ? Groups.FirstOrDefault(g => string.Equals(g.Folder, selected.GroupFolder, StringComparison.OrdinalIgnoreCase)) : null;
        if (SelectedTab is { } tab && group is not null)
        {
            _lastSessionTab[group.Folder] = tab;
        }
        var session = SelectedTab
            ?? (group is null ? null : _lastSessionTab.TryGetValue(group.Folder, out var last) && group.Tabs.Contains(last) ? last : group.Tabs.FirstOrDefault());
        var named = session is not null && (!ReferenceEquals(session, Selected) || IsSplit);
        ToolWindows.ShowSession(session, named, group?.Color);
        TellTabsWhatShows();
    }

    /// <summary>The windows on screen changed, or the session they show did: each tab hears which show its data.</summary>
    private void TellTabsWhatShows()
    {
        var session = ToolWindows.Session;
        if (_toolSession is { } was && !ReferenceEquals(was, session))
        {
            was.ShowToolWindows(new HashSet<string>());
        }
        _toolSession = session;
        session?.ShowToolWindows(ToolWindows.ShowingKinds);
    }

    /// <summary>A game opened or closed: its window comes, or goes with the game.</summary>
    private void OnGameOpenChanged() => ToolWindows.For(ToolWindowKinds.Games).IsAvailable = _services.Games.IsOpen;

    /// <summary>The command palette's entries for the tool windows: each that has one, shown as its tab's click does.</summary>
    private IEnumerable<PaletteEntry> ToolWindowEntries() =>
        ToolWindows.Windows
            .Where(w => w.Kind.PaletteText is not null && w.IsAvailable)
            .Select(w => new PaletteEntry(w.Kind.PaletteText!, "Command", () =>
            {
                ToolWindows.Show(w);
                return Task.CompletedTask;
            }, null, null));
}
