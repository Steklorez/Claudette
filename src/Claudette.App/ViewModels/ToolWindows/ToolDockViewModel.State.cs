using Claudette.Core.Settings;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The layout kept with the app state (DESIGN.md §3, "Tool windows", "Saved"): each window's edge, place and whether it's
/// open, and each edge's size, state and tab. The side panel's width and order, saved before tool windows, carry over.
/// </summary>
public sealed partial class ToolDockViewModel
{
    /// <summary>
    /// The layout as it was saved, or with none saved yet, the windows open as tabs along the right edge, auto-hidden, in
    /// the side panel's order and at its width when the user had chosen them.
    /// </summary>
    private void Restore()
    {
        if (_state.ToolWindows is { } saved)
        {
            RestoreSaved(saved);
            return;
        }
        Arrange(_state.SidePanelPages);
        Right.Size = Math.Max(_state.SidePanelWidth ?? DefaultRightWidth, MinRightWidth);
        // The tabs along the edge take no room from the conversation, and show the windows are there.
        Right.IsAutoHide = true;
    }

    private void RestoreSaved(ToolWindowsState saved)
    {
        var chosen = new Dictionary<ToolEdge, string?>();
        foreach (var state in saved.Edges)
        {
            if (Parse(state.Edge) is not { } which)
            {
                continue;
            }
            var edge = EdgeOf(which);
            edge.Size = Math.Max(state.Size ?? edge.DefaultSize, edge.MinSize);
            edge.IsAutoHide = state.AutoHide;
            chosen[which] = state.Selected;
        }
        var placed = new HashSet<ToolWindowViewModel>();
        foreach (var state in saved.Windows)
        {
            if (Parse(state.Edge) is not { } which)
            {
                continue;
            }
            if (ToolWindowKinds.Find(state.Id) is { IsMultiple: true } multiple)
            {
                _kindEdges[multiple.Id] = which;
                continue;
            }
            if (Find(state.Id) is not { } window || !placed.Add(window))
            {
                continue;
            }
            window.Edge = which;
            window.IsOpen = state.IsOpen;
            EdgeOf(which).Windows.Add(window);
        }
        PlaceTheRest(placed);
        foreach (var edge in Edges)
        {
            edge.Chosen = chosen.GetValueOrDefault(edge.Edge) is { } id && Find(id) is { } window && window.Edge == edge.Edge ? window : null;
        }
    }

    /// <summary>
    /// Each window on its own edge, in <paramref name="order"/> (the side panel's pages, by name) as far as it goes.
    /// A name no kind has is left out.
    /// </summary>
    private void Arrange(IEnumerable<string>? order)
    {
        var placed = new HashSet<ToolWindowViewModel>();
        foreach (var name in order ?? [])
        {
            if (Find(name) is { } window && placed.Add(window))
            {
                window.Edge = window.Kind.DefaultEdge;
                EdgeOf(window.Edge).Windows.Add(window);
            }
        }
        PlaceTheRest(placed);
    }

    /// <summary>
    /// The windows a saved layout doesn't have, such as a kind added since: each on its own edge, open, after the window it
    /// follows by default there, or first.
    /// </summary>
    private void PlaceTheRest(HashSet<ToolWindowViewModel> placed)
    {
        var kinds = ToolWindowKinds.All.Where(k => !k.IsMultiple).ToList();
        for (var i = 0; i < kinds.Count; i++)
        {
            if (Find(kinds[i].Id) is not { } window || placed.Contains(window))
            {
                continue;
            }
            window.Edge = kinds[i].DefaultEdge;
            var edge = EdgeOf(window.Edge);
            var after = kinds.Take(i).Reverse().Select(k => Find(k.Id)).FirstOrDefault(w => w is not null && edge.Windows.Contains(w));
            edge.Windows.Insert(after is null ? 0 : edge.Windows.IndexOf(after) + 1, window);
            placed.Add(window);
        }
    }

    private static ToolEdge? Parse(string name) => Enum.TryParse<ToolEdge>(name, out var edge) && Enum.IsDefined(edge) ? edge : null;

    /// <summary>Keeps the layout, in place of the side panel's width and order.</summary>
    private void Save()
    {
        if (!_ready)
        {
            return;
        }
        _state.ToolWindows = new ToolWindowsState
        {
            Edges = [.. Edges.Select(e => new ToolEdgeState { Edge = e.Edge.ToString(), Size = e.Size, AutoHide = e.IsAutoHide, Selected = e.Chosen?.Id })],
            Windows =
            [
                .. Edges.SelectMany(e => e.Windows.Where(w => !w.Kind.IsMultiple)
                    .Select(w => new ToolWindowState { Id = w.Id, Edge = e.Edge.ToString(), IsOpen = w.IsOpen })),
                // Where a kind with several windows docks its next one.
                .. _kindEdges.Select(k => new ToolWindowState { Id = k.Key, Edge = k.Value.ToString(), IsOpen = false }),
            ],
        };
        _state.SidePanelWidth = null;
        _state.SidePanelPages = null;
        _saveState();
    }

    /// <summary>
    /// The Tool windows menu's <b>Reset layout</b>: every window open on its own edge in its first order, the edges at
    /// their first sizes, the right one auto-hidden, the bottom one docked.
    /// </summary>
    public void ResetLayout()
    {
        CancelSlideTimers();
        EndSlide();
        foreach (var edge in Edges)
        {
            edge.Windows.Clear();
            edge.Size = edge.DefaultSize;
            edge.Chosen = null;
        }
        Right.IsAutoHide = true;
        Bottom.IsAutoHide = false;
        _kindEdges.Clear();
        foreach (var window in _windows)
        {
            window.IsOpen = true;
        }
        Arrange(null);
        foreach (var window in _windows.Where(w => w.Kind.IsMultiple))
        {
            window.Edge = window.Kind.DefaultEdge;
            EdgeOf(window.Edge).Windows.Add(window);
        }
        foreach (var window in _windows)
        {
            window.OnEdgeStateChanged();
        }
        Refresh();
        Save();
    }
}
