using System.ComponentModel;

namespace Claudette.App.ViewModels;

/// <summary>
/// What every tab in the sidebar and the panes is, whether a session's (<see cref="TabViewModel"/>) or a folder group's
/// Project Files tab (<see cref="ProjectFilesViewModel"/>, DESIGN.md §18, "Project files"): what the selection, the panes
/// and the restart snapshot need to know of it.
/// </summary>
public interface IShellTab : INotifyPropertyChanged
{
    /// <summary>Its id, kept with the app state and in the restart snapshot.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>The folder of the group it's in.</summary>
    string GroupFolder { get; }

    /// <summary>The tab the keyboard and the header follow: the focused pane's.</summary>
    bool IsSelected { get; set; }

    /// <summary>On screen, in a pane.</summary>
    bool IsShown { get; set; }

    /// <summary>It shares the window with other tabs, side by side, and its pane has a header line.</summary>
    bool IsInSplit { get; set; }

    /// <summary>Its place among the panes, from the left; -1 when it isn't in one.</summary>
    int PaneIndex { get; set; }

    /// <summary>
    /// Its group's color, which its pane's header shows with tabs side by side, so each pane's project can be told without
    /// reading it; null outside a group. The shell keeps it.
    /// </summary>
    Avalonia.Media.Color? PaneColor { get; set; }

    /// <summary>Its row is being dragged in the sidebar.</summary>
    bool IsDragged { get; set; }
}
