using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// An edge of the main window and the tool windows docked at it (DESIGN.md §3, "Tool windows"): one group of tabs, docked
/// beside the panes or auto-hidden as tabs along the window's edge, with its own size. Pinning or unpinning is the
/// edge's, so it applies to all its windows, as in Visual Studio.
/// </summary>
public sealed partial class ToolEdgeViewModel : ViewModelBase
{
    private readonly ToolDockViewModel _dock;

    internal ToolEdgeViewModel(ToolDockViewModel dock, ToolEdge edge)
    {
        _dock = dock;
        Edge = edge;
        Size = DefaultSize;
    }

    public ToolEdge Edge { get; }

    public ToolDockViewModel Dock => _dock;

    public bool IsRight => Edge == ToolEdge.Right;

    public bool IsBottom => Edge == ToolEdge.Bottom;

    /// <summary>How the menus name it: <i>right</i> or <i>bottom</i>.</summary>
    public string Name => IsRight ? "right" : "bottom";

    /// <summary>Its windows in their order along it, open or closed, with something to show or not.</summary>
    internal List<ToolWindowViewModel> Windows { get; } = [];

    /// <summary>Its tabs: the windows open on it that have something to show, in order.</summary>
    public ObservableCollection<ToolWindowViewModel> Tabs { get; } = [];

    /// <summary>It has a tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocked), nameof(HasStrip))]
    public partial bool HasTabs { get; private set; }

    // ---- Size ----------------------------------------------------------------------------------------------------

    /// <summary>The right edge's width as it docks at first, or the bottom's height.</summary>
    public double DefaultSize => IsRight ? ToolDockViewModel.DefaultRightWidth : ToolDockViewModel.DefaultBottomHeight;

    /// <summary>The least the user can drag it to.</summary>
    public double MinSize => IsRight ? ToolDockViewModel.MinRightWidth : ToolDockViewModel.MinBottomHeight;

    /// <summary>The right edge's width or the bottom's height, as the user dragged it: docked, and slid out.</summary>
    [ObservableProperty]
    public partial double Size { get; internal set; }

    // ---- Docked or auto-hidden -----------------------------------------------------------------------------------

    /// <summary>Its windows fold into tabs along the window's edge and slide out over the conversation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocked), nameof(HasStrip), nameof(PinName), nameof(PinTip))]
    public partial bool IsAutoHide { get; internal set; }

    /// <summary>Docked beside the panes, taking room of its own.</summary>
    public bool IsDocked => !IsAutoHide && HasTabs;

    /// <summary>Auto-hidden, with tabs along the window's edge.</summary>
    public bool HasStrip => IsAutoHide && HasTabs;

    /// <summary>The pin's name: what clicking it does.</summary>
    public string PinName => IsAutoHide ? "Dock" : "Auto-hide";

    public string PinTip => IsAutoHide
        ? $"Dock: keep these windows open at the {Name}, beside the conversation"
        : $"Auto-hide: fold these windows into tabs along the {Name} edge, which slide out when pointed at";

    /// <summary>The pin on its header: docked to auto-hidden and back, for all its windows.</summary>
    [RelayCommand]
    private void TogglePin() => _dock.SetAutoHide(Edge, !IsAutoHide);

    // ---- What shows ----------------------------------------------------------------------------------------------

    /// <summary>The tab the user chose last, which shows while it has a tab; another shows while it hasn't.</summary>
    internal ToolWindowViewModel? Chosen { get; set; }

    /// <summary>Its docked group's tab showing: the one chosen, while it has a tab, else the first.</summary>
    [ObservableProperty]
    public partial ToolWindowViewModel? Selected { get; private set; }

    /// <summary>While it's auto-hidden: the window slid out over the conversation, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlidOut))]
    public partial ToolWindowViewModel? SlidOut { get; internal set; }

    public bool IsSlidOut => SlidOut is not null;

    /// <summary>The window on screen at this edge: docked, its selected tab; auto-hidden, the one slid out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionLabel), nameof(CloseName))]
    public partial ToolWindowViewModel? Showing { get; private set; }

    /// <summary>The × on its header: what it closes.</summary>
    public string CloseName => Showing is { } showing ? $"Close {showing.Title}" : "Close";

    /// <summary>The × on its header.</summary>
    [RelayCommand]
    private void CloseShowing()
    {
        if (Showing is { } showing)
        {
            _dock.Close(showing);
        }
    }

    /// <summary>
    /// Over a window that shows a session's data, while it might not be the one the user is looking at (a Project Files tab
    /// or a log has the focus, or tabs are side by side): that session's name, as its pane's header says it.
    /// </summary>
    public string? SessionLabel => Showing is { Kind.FollowsSession: true } && _dock.NamesSession ? _dock.Session?.DisplayName : null;

    /// <summary>The group color of the session <see cref="SessionLabel"/> names.</summary>
    public Avalonia.Media.Color? SessionColor => _dock.SessionColor;

    internal void OnSessionChanged()
    {
        OnPropertyChanged(nameof(SessionLabel));
        OnPropertyChanged(nameof(SessionColor));
    }

    /// <summary>
    /// Puts the tabs in step with its windows, and chooses what shows: the chosen tab while it has one, else the first;
    /// slid out, only a window that still has its tab.
    /// </summary>
    internal void Refresh()
    {
        var tabs = Windows.Where(w => w.HasTab).ToList();
        for (var i = 0; i < tabs.Count; i++)
        {
            var at = Tabs.IndexOf(tabs[i]);
            if (at < 0)
            {
                Tabs.Insert(i, tabs[i]);
            }
            else if (at != i)
            {
                Tabs.Move(at, i);
            }
        }
        while (Tabs.Count > tabs.Count)
        {
            Tabs.RemoveAt(Tabs.Count - 1);
        }
        HasTabs = Tabs.Count > 0;
        Selected = Chosen is { HasTab: true } chosen && Tabs.Contains(chosen) ? chosen : Tabs.FirstOrDefault();
        if (SlidOut is { } slid && (!IsAutoHide || !Tabs.Contains(slid)))
        {
            SlidOut = null;
        }
        Showing = IsAutoHide ? SlidOut : Selected;
        foreach (var window in Windows)
        {
            window.IsSelected = ReferenceEquals(window, Selected);
            window.IsShowing = ReferenceEquals(window, Showing);
        }
        OnSessionChanged();
    }

    public override string ToString() => Name;
}
