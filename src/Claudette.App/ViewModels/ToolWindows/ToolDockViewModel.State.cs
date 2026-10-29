using Claudette.Core.Settings;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The layout kept with the app state (DESIGN.md §3, "Tool windows", "Kept"): each window's group, place in it, whether
/// it shows and its view mode, and the sizes. The side panel's width and order, saved before tool windows, carry over, as
/// do the edges' sizes and windows saved before the strips.
/// </summary>
public sealed partial class ToolDockViewModel
{
    /// <summary>
    /// The layout as it was saved, or with none saved yet, each window in its own group in the side panel's order as far as
    /// the user had chosen one, at the side panel's width, and none showing.
    /// </summary>
    private void Restore()
    {
        if (_state.ToolWindows is { } saved)
        {
            RestoreSaved(saved);
            return;
        }
        Arrange(_state.SidePanelPages);
        RightWidth = Math.Max(_state.SidePanelWidth ?? DefaultRightWidth, MinRightWidth);
    }

    private void RestoreSaved(ToolWindowsState saved)
    {
        // A layout from before the strips kept each edge's size.
        var edges = saved.Edges ?? [];
        var rightWidth = saved.RightWidth ?? edges.FirstOrDefault(e => e.Edge == "Right")?.Size;
        var bottomHeight = saved.BottomHeight ?? edges.FirstOrDefault(e => e.Edge == "Bottom")?.Size;
        RightWidth = Math.Max(rightWidth ?? DefaultRightWidth, MinRightWidth);
        BottomHeight = Math.Max(bottomHeight ?? DefaultBottomHeight, MinBottomHeight);
        SplitShare = Math.Clamp(saved.SplitShare ?? DefaultSplitShare, MinSplitShare, 1 - MinSplitShare);
        var placed = new HashSet<ToolWindowViewModel>();
        foreach (var state in saved.Windows)
        {
            if (Find(state.Id) is not { } window || GroupNamed(state, window.Kind) is not { } group || !placed.Add(window))
            {
                continue;
            }
            window.Group = group;
            window.IsPinned = state.IsPinned;
            var at = GroupOf(group);
            at.Windows.Add(window);
            if (state.IsShowing && state.Group is not null)
            {
                at.Chosen = window;
            }
        }
        PlaceTheRest(placed);
    }

    /// <summary>
    /// A saved window's group, or for one saved before the strips, its edge's: the bottom's, or for the right, which had
    /// no split, the right-side group its kind starts in, so Scratch pad, Processes and Games split the right side.
    /// </summary>
    private static ToolGroup? GroupNamed(ToolWindowState state, ToolWindowKind kind) =>
        state.Group is { } name ? Enum.TryParse<ToolGroup>(name, out var group) && Enum.IsDefined(group) ? group : null
        : state.Edge switch
        {
            "Right" => kind.DefaultGroup == ToolGroup.Bottom ? ToolGroup.Top : kind.DefaultGroup,
            "Bottom" => ToolGroup.Bottom,
            _ => null,
        };

    /// <summary>
    /// Each window in its own group, in <paramref name="order"/> (the side panel's pages, by name) as far as it goes.
    /// A name no kind has is left out.
    /// </summary>
    private void Arrange(IEnumerable<string>? order)
    {
        var placed = new HashSet<ToolWindowViewModel>();
        foreach (var name in order ?? [])
        {
            if (Find(name) is { } window && placed.Add(window))
            {
                window.Group = window.Kind.DefaultGroup;
                GroupOf(window.Group).Windows.Add(window);
            }
        }
        PlaceTheRest(placed);
    }

    /// <summary>
    /// The windows a saved layout doesn't have, such as a kind added since: each in its own group, after the window it
    /// follows by default there, or first.
    /// </summary>
    private void PlaceTheRest(HashSet<ToolWindowViewModel> placed)
    {
        var kinds = ToolWindowKinds.All;
        for (var i = 0; i < kinds.Count; i++)
        {
            var window = For(kinds[i]);
            if (placed.Contains(window))
            {
                continue;
            }
            window.Group = kinds[i].DefaultGroup;
            var group = GroupOf(window.Group);
            var after = kinds.Take(i).Reverse().Select(For).FirstOrDefault(group.Windows.Contains);
            group.Windows.Insert(after is null ? 0 : group.Windows.IndexOf(after) + 1, window);
            placed.Add(window);
        }
    }

    /// <summary>Keeps the layout, in place of the side panel's width and order.</summary>
    private void Save()
    {
        if (!_ready)
        {
            return;
        }
        _state.ToolWindows = new ToolWindowsState
        {
            RightWidth = RightWidth,
            BottomHeight = BottomHeight,
            SplitShare = SplitShare,
            Windows =
            [
                .. Groups.SelectMany(g => g.Windows.Select(w => new ToolWindowState
                {
                    Id = w.Id,
                    Group = g.Group.ToString(),
                    IsShowing = ReferenceEquals(g.Chosen, w),
                    IsPinned = w.IsPinned,
                })),
            ],
        };
        _state.SidePanelWidth = null;
        _state.SidePanelPages = null;
        _saveState();
    }

    /// <summary>
    /// The command palette's <b>Reset tool windows</b>: every window in its own group in its first order, pinned and hidden,
    /// and the sizes as at first.
    /// </summary>
    public void ResetLayout()
    {
        foreach (var group in Groups)
        {
            group.Windows.Clear();
            group.Chosen = null;
        }
        foreach (var window in _windows)
        {
            window.IsPinned = true;
        }
        RightWidth = DefaultRightWidth;
        BottomHeight = DefaultBottomHeight;
        SplitShare = DefaultSplitShare;
        Arrange(null);
        Refresh();
        Save();
    }
}
