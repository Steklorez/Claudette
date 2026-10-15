using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Tabs side by side (DESIGN.md §18, "Split panes"): up to <see cref="MostPanes"/> of them on screen at once, the
/// selected tab's pane the focused one. A tab picked that isn't on screen takes the focused pane's place. Panes the
/// window has no room for fold away, and come back as it widens.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The most tabs side by side.</summary>
    public const int MostPanes = 3;

    /// <summary>The tabs the user put side by side, left to right, whether or not the window has room for them all now.</summary>
    private readonly List<TabViewModel> _panes = [];

    /// <summary>The tabs on screen, left to right.</summary>
    public ObservableCollection<TabViewModel> Panes { get; } = [];

    /// <summary>More than one tab is on screen.</summary>
    public bool IsSplit => Panes.Count > 1;

    /// <summary>
    /// The selection moved to <paramref name="tab"/>: one already on screen is only focused; any other takes the place of
    /// the pane that had the focus.
    /// </summary>
    private void PlaceSelected(TabViewModel? previous, TabViewModel? tab)
    {
        if (tab is null)
        {
            // Nothing selected, nothing on screen.
            _panes.Clear();
            return;
        }
        if (_panes.Contains(tab))
        {
            return;
        }
        var at = previous is null ? -1 : _panes.IndexOf(previous);
        if (at >= 0)
        {
            _panes[at] = tab;
        }
        else if (_panes.Count > 0)
        {
            _panes[^1] = tab;
        }
        else
        {
            _panes.Add(tab);
        }
    }

    /// <summary>Which tabs are on screen, and where: every tab hears whether it's in a pane, and which.</summary>
    private void UpdatePanes()
    {
        _panes.RemoveAll(t => !OpenTabs.Contains(t));
        if (SelectedTab is { } selected && !_panes.Contains(selected))
        {
            _panes.Add(selected);
        }
        var shown = Fit(Layout.MaxPanes);
        var split = shown.Count > 1;
        foreach (var tab in OpenTabs)
        {
            var index = shown.IndexOf(tab);
            tab.IsInSplit = split && index >= 0;
            tab.PaneIndex = index;
            tab.IsShown = index >= 0;
        }
        if (!Panes.SequenceEqual(shown))
        {
            Panes.Clear();
            foreach (var tab in shown)
            {
                Panes.Add(tab);
            }
            OnPropertyChanged(nameof(IsSplit));
        }
        var ids = _panes.Count > 1 ? _panes.Select(t => t.Id).ToList() : null;
        if (!(ids ?? []).SequenceEqual(_services.State.PaneTabIds ?? []))
        {
            _services.State.PaneTabIds = ids;
            _services.SaveState();
        }
    }

    /// <summary>
    /// The panes there's room for, <paramref name="room"/> of them side by side: the focused pane and its neighbours, in
    /// their order.
    /// </summary>
    private List<TabViewModel> Fit(int room)
    {
        if (_panes.Count <= room)
        {
            return [.. _panes];
        }
        var focused = SelectedTab is null ? 0 : Math.Max(0, _panes.IndexOf(SelectedTab));
        var start = Math.Clamp(focused - (room - 1), 0, _panes.Count - room);
        return _panes.GetRange(start, room);
    }

    /// <summary>
    /// <b>Open beside</b>: <paramref name="tab"/> in a pane of its own after the focused one, which it takes the focus
    /// from. With every pane taken, it takes the place of the last one that isn't focused.
    /// </summary>
    [RelayCommand]
    private void OpenBeside(TabViewModel? tab)
    {
        if (tab is null || !OpenTabs.Contains(tab))
        {
            return;
        }
        if (!_panes.Contains(tab))
        {
            var after = SelectedTab is null ? -1 : _panes.IndexOf(SelectedTab);
            if (_panes.Count >= MostPanes && _panes.LastOrDefault(p => !ReferenceEquals(p, SelectedTab)) is { } replaced)
            {
                var at = _panes.IndexOf(replaced);
                _panes.RemoveAt(at);
                if (at <= after)
                {
                    after--;
                }
            }
            _panes.Insert(Math.Clamp(after + 1, 0, _panes.Count), tab);
        }
        if (ReferenceEquals(SelectedTab, tab))
        {
            UpdatePanes();
        }
        else
        {
            SelectedTab = tab;
        }
    }

    /// <summary><b>Close pane</b>: the tab stays open, out of sight, and the pane beside it takes the focus if it had it.</summary>
    [RelayCommand]
    private void ClosePane(TabViewModel? tab)
    {
        if (tab is null || _panes.Count < 2 || _panes.IndexOf(tab) is not (>= 0 and var at))
        {
            return;
        }
        _panes.RemoveAt(at);
        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = _panes[Math.Min(at, _panes.Count - 1)];
        }
        else
        {
            UpdatePanes();
        }
    }

    /// <summary><b>Close other panes</b>: only the focused tab stays on screen.</summary>
    [RelayCommand]
    private void CloseOtherPanes()
    {
        _panes.RemoveAll(p => !ReferenceEquals(p, SelectedTab));
        UpdatePanes();
    }

    /// <summary>
    /// <b>Show with its sub-threads</b>: a thread beside its first sub-threads (DESIGN.md §18, "Threads"), as many as
    /// there are panes for, the thread focused.
    /// </summary>
    [RelayCommand]
    private void ShowThreadSideBySide(TabViewModel? tab)
    {
        if ((tab?.ThreadHead ?? tab) is not { } head || head.SubThreads.Count == 0)
        {
            return;
        }
        _panes.Clear();
        _panes.Add(head);
        _panes.AddRange(head.SubThreads.Where(OpenTabs.Contains).Take(MostPanes - 1));
        if (ReferenceEquals(SelectedTab, head))
        {
            UpdatePanes();
        }
        else
        {
            SelectedTab = head;
        }
    }

    /// <summary>The panes a restored window had, before its selected tab is chosen.</summary>
    private void RestorePanes(IEnumerable<string>? ids)
    {
        _panes.Clear();
        foreach (var id in ids ?? [])
        {
            if (OpenTabs.FirstOrDefault(t => t.Id == id) is { } tab && !_panes.Contains(tab) && _panes.Count < MostPanes)
            {
                _panes.Add(tab);
            }
        }
    }
}
