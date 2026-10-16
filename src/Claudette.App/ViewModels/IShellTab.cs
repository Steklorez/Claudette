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

    /// <summary>Its side panel shows, which its pane keeps room for.</summary>
    bool ShowsSidePanel { get; }

    /// <summary>Its row is being dragged in the sidebar.</summary>
    bool IsDragged { get; set; }
}
