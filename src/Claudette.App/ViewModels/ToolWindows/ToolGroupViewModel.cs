using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// A group of icons on the tool window strip (DESIGN.md §3, "Tool windows"), and the panel its window shows in: the right
/// side's top, its split under it, or the bottom under the panes. One of its windows shows at a time, as in Rider.
/// </summary>
public sealed partial class ToolGroupViewModel : ViewModelBase
{
    private readonly ToolDockViewModel _dock;

    internal ToolGroupViewModel(ToolDockViewModel dock, ToolGroup group)
    {
        _dock = dock;
        Group = group;
    }

    public ToolGroup Group { get; }

    public ToolDockViewModel Dock => _dock;

    /// <summary>How the menus and a screen reader name it: <i>right top</i>, <i>right bottom</i> or <i>bottom</i>.</summary>
    public string Name => Group switch
    {
        ToolGroup.Top => "right top",
        ToolGroup.Split => "right bottom",
        _ => "bottom",
    };

    /// <summary>Its windows in their order on the strip, with something to show or not.</summary>
    internal List<ToolWindowViewModel> Windows { get; } = [];

    /// <summary>Its icons on the strip: its windows that have something to show, in order.</summary>
    public ObservableCollection<ToolWindowViewModel> Icons { get; } = [];

    /// <summary>It has an icon on the strip.</summary>
    [ObservableProperty]
    public partial bool HasIcons { get; private set; }

    /// <summary>The window chosen to show, which stays chosen while it has nothing to show and shows again once it has.</summary>
    internal ToolWindowViewModel? Chosen { get; set; }

    /// <summary>Its window on screen: the one chosen, while it has its icon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowing), nameof(SessionLabel), nameof(HideName), nameof(OptionsName))]
    public partial ToolWindowViewModel? Showing { get; private set; }

    public bool IsShowing => Showing is not null;

    /// <summary>The <b>−</b> on its header: what it hides.</summary>
    public string HideName => Showing is { } showing ? $"Hide {showing.Title}" : "Hide";

    /// <summary>The <b>⋮</b> on its header: whose options it has.</summary>
    public string OptionsName => Showing is { } showing ? $"{showing.Title} options" : "Options";

    /// <summary>The <b>−</b> on its header.</summary>
    [RelayCommand]
    private void HideShowing()
    {
        if (Showing is { } showing)
        {
            _dock.Hide(showing);
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

    /// <summary>Puts the icons in step with its windows, and shows the one chosen while it has its icon.</summary>
    internal void Refresh()
    {
        var icons = Windows.Where(w => w.IsAvailable).ToList();
        for (var i = 0; i < icons.Count; i++)
        {
            var at = Icons.IndexOf(icons[i]);
            if (at < 0)
            {
                Icons.Insert(i, icons[i]);
            }
            else if (at != i)
            {
                Icons.Move(at, i);
            }
        }
        while (Icons.Count > icons.Count)
        {
            Icons.RemoveAt(Icons.Count - 1);
        }
        HasIcons = Icons.Count > 0;
        Showing = Chosen is { IsAvailable: true } chosen && Icons.Contains(chosen) ? chosen : null;
        foreach (var window in Windows)
        {
            window.IsShowing = ReferenceEquals(window, Showing);
        }
        OnSessionChanged();
    }

    public override string ToString() => Name;
}
