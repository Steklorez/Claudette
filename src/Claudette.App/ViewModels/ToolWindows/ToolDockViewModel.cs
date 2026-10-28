using Claudette.App.Services;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The tool windows (DESIGN.md §3, "Tool windows"), as Visual Studio has them: one layout for the whole main window, at
/// its right and bottom edges, outside the panes. Each window has a tab on its edge; an edge is docked, beside the panes
/// with a size of its own, or auto-hidden, its tabs along the window's edge sliding their window out over the
/// conversation. The windows that follow the session show the focused session tab's data. Partials: the session
/// (<c>.Session</c>), sliding out (<c>.Slide</c>), dragging a tab to another edge (<c>.Drop</c>), and saving the layout
/// (<c>.State</c>).
/// </summary>
public sealed partial class ToolDockViewModel : ViewModelBase, IDisposable
{
    /// <summary>The right edge's width at first: the side panel's, which tool windows replaced.</summary>
    public const double DefaultRightWidth = 340;

    /// <summary>The least the right edge can be dragged to: the side panel's.</summary>
    public const double MinRightWidth = 260;

    public const double DefaultBottomHeight = 240;

    public const double MinBottomHeight = 120;

    /// <summary>The panes keep at least this height above a docked bottom edge.</summary>
    public const double MinPanesHeight = 200;

    /// <summary>How much room an auto-hidden edge's tabs take along the window's edge.</summary>
    public const double StripThickness = 26;

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly List<ToolWindowViewModel> _windows = [];

    /// <summary>Set once the layout is restored: until then, nothing is worked out or saved.</summary>
    private readonly bool _ready;

    /// <param name="saveState">Saves <paramref name="state"/> once the layout changes.</param>
    internal ToolDockViewModel(AppState state, Action saveState, TimeProvider time, IUiDispatcher dispatcher)
    {
        _state = state;
        _saveState = saveState;
        _slideOutTimer = new UiTimeout(time, dispatcher);
        _slideBackTimer = new UiTimeout(time, dispatcher);
        Right = new ToolEdgeViewModel(this, ToolEdge.Right);
        Bottom = new ToolEdgeViewModel(this, ToolEdge.Bottom);
        foreach (var kind in ToolWindowKinds.All.Where(k => !k.IsMultiple))
        {
            _windows.Add(new ToolWindowViewModel(this, kind, kind.Id, kind.Title, content: null));
        }
        Restore();
        _ready = true;
        Refresh();
    }

    public ToolEdgeViewModel Right { get; }

    public ToolEdgeViewModel Bottom { get; }

    public IReadOnlyList<ToolEdgeViewModel> Edges => [Right, Bottom];

    /// <summary>Every window, open or closed.</summary>
    public IReadOnlyList<ToolWindowViewModel> Windows => _windows;

    public ToolEdgeViewModel EdgeOf(ToolEdge edge) => edge == ToolEdge.Right ? Right : Bottom;

    public ToolWindowViewModel? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    /// <summary>The window of a kind that has one.</summary>
    public ToolWindowViewModel For(ToolWindowKind kind) => Find(kind.Id) ?? throw new ArgumentException($"{kind.Id} has no window of its own.", nameof(kind));

    /// <summary>
    /// The least room the right edge takes beside the panes, which the sidebar and the panes leave it (DESIGN.md §4,
    /// "Sidebar"; §18, "Split panes"): docked, its least width; auto-hidden, its tabs; nothing without windows.
    /// </summary>
    [ObservableProperty]
    public partial double RightRoom { get; private set; }

    /// <summary>
    /// What's on screen changed: a window came or went, or the session the windows show is another. Each tab hears which
    /// of its windows show, for the work they need done while they do.
    /// </summary>
    public event Action? ShowingChanged;

    /// <summary>The kinds of the windows on screen.</summary>
    public IReadOnlySet<string> ShowingKinds => _windows.Where(w => w.IsShowing).Select(w => w.Kind.Id).ToHashSet();

    /// <summary>The edges' tabs and what shows follow the windows, and every window and tab hears what changed.</summary>
    internal void Refresh()
    {
        if (!_ready)
        {
            return;
        }
        Right.Refresh();
        Bottom.Refresh();
        RightRoom = Right.IsDocked ? MinRightWidth : Right.HasStrip ? StripThickness : 0;
        ShowingChanged?.Invoke();
    }

    // ---- Showing, hiding and closing -----------------------------------------------------------------------------

    /// <summary>
    /// The way to a window from a button, a menu or the command palette: docked, its tab shows; auto-hidden, it slides
    /// out, until the pointer is pressed elsewhere; closed, it opens where it was, and shows.
    /// </summary>
    public void Show(ToolWindowKind kind) => Show(For(kind));

    /// <inheritdoc cref="Show(ToolWindowKind)"/>
    public void Show(ToolWindowViewModel window)
    {
        if (!_windows.Contains(window))
        {
            return;
        }
        var edge = EdgeOf(window.Edge);
        var opened = !window.IsOpen;
        window.IsOpen = true;
        edge.Chosen = window;
        Refresh();
        if (edge.IsAutoHide)
        {
            SlideOut(window, held: true);
        }
        if (opened || !edge.IsAutoHide)
        {
            Save();
        }
    }

    /// <summary>
    /// The Tool windows menu's entry: a window on screen hides, slid out sliding back and docked closing; any other shows,
    /// as <see cref="Show(ToolWindowViewModel)"/> says.
    /// </summary>
    public void Toggle(ToolWindowViewModel window)
    {
        if (!window.IsShowing)
        {
            Show(window);
        }
        else if (EdgeOf(window.Edge).IsAutoHide)
        {
            SlideBack();
        }
        else
        {
            Close(window);
        }
    }

    /// <summary>Its tab clicked in a docked group: it shows, and is the tab the edge shows from now on.</summary>
    public void Select(ToolWindowViewModel window)
    {
        if (!window.HasTab)
        {
            return;
        }
        var edge = EdgeOf(window.Edge);
        if (edge.IsAutoHide)
        {
            ClickTab(window);
            return;
        }
        edge.Chosen = window;
        Refresh();
        Save();
    }

    /// <summary>
    /// Its ×: its tab goes, and the tab beside it shows. It comes back where it was from the Tool windows menu, or
    /// whatever shows it. A window of a kind that has several is gone for good (<see cref="Remove"/>).
    /// </summary>
    public void Close(ToolWindowViewModel window)
    {
        if (window.Kind.IsMultiple)
        {
            Remove(window);
            return;
        }
        var edge = EdgeOf(window.Edge);
        if (ReferenceEquals(edge.Selected, window) && edge.Tabs.IndexOf(window) is >= 0 and var at)
        {
            edge.Chosen = edge.Tabs.ElementAtOrDefault(at + 1) ?? edge.Tabs.ElementAtOrDefault(at - 1);
        }
        if (ReferenceEquals(edge.SlidOut, window))
        {
            EndSlide();
        }
        window.IsOpen = false;
        Refresh();
        Save();
    }

    /// <summary>
    /// The pin on an edge's header: docked, its windows fold into tabs along the window's edge; auto-hidden, they dock
    /// beside the panes, the one slid out showing. As in Visual Studio, it's all the edge's windows.
    /// </summary>
    public void SetAutoHide(ToolEdge which, bool autoHide)
    {
        var edge = EdgeOf(which);
        if (edge.IsAutoHide == autoHide)
        {
            return;
        }
        if (edge.SlidOut is { } slid)
        {
            edge.Chosen = slid;
        }
        if (SlidOut is not null)
        {
            CancelSlideTimers();
            EndSlide();
        }
        edge.IsAutoHide = autoHide;
        foreach (var window in edge.Windows)
        {
            window.OnEdgeStateChanged();
        }
        Refresh();
        Save();
    }

    // ---- Windows made at run time ----------------------------------------------------------------------------------

    /// <summary>Where the next window of a kind that has several docks: where the user last moved one, else its edge.</summary>
    private readonly Dictionary<string, ToolEdge> _kindEdges = [];

    private int _made;

    /// <summary>
    /// Makes a window of a kind that has several (<see cref="ToolWindowKind.IsMultiple"/>), such as a search's results,
    /// last on the edge the kind's windows dock at, and shows it unless <paramref name="show"/> is false. Its × removes it.
    /// </summary>
    public ToolWindowViewModel Add(ToolWindowKind kind, string title, object? content, bool show = true)
    {
        if (!kind.IsMultiple)
        {
            throw new ArgumentException($"{kind.Id} has one window, made with the dock.", nameof(kind));
        }
        var window = new ToolWindowViewModel(this, kind, $"{kind.Id}#{++_made}", title, content)
        {
            Edge = _kindEdges.TryGetValue(kind.Id, out var edge) ? edge : kind.DefaultEdge,
        };
        _windows.Add(window);
        var at = EdgeOf(window.Edge);
        // The first window on an empty edge docks it, as dragging one there does.
        if (!at.HasTabs)
        {
            at.IsAutoHide = false;
        }
        at.Windows.Add(window);
        if (show)
        {
            Show(window);
        }
        else
        {
            Refresh();
        }
        return window;
    }

    /// <summary>A window of a kind that has several was closed, by its × or by what made it.</summary>
    public event Action<ToolWindowViewModel>? Removed;

    /// <summary>Takes a window made at run time away for good.</summary>
    public void Remove(ToolWindowViewModel window)
    {
        if (!window.Kind.IsMultiple || !_windows.Remove(window))
        {
            return;
        }
        var edge = EdgeOf(window.Edge);
        if (ReferenceEquals(edge.Selected, window) && edge.Tabs.IndexOf(window) is >= 0 and var at)
        {
            edge.Chosen = edge.Tabs.ElementAtOrDefault(at + 1) ?? edge.Tabs.ElementAtOrDefault(at - 1);
        }
        if (ReferenceEquals(edge.SlidOut, window))
        {
            EndSlide();
        }
        edge.Windows.Remove(window);
        window.IsOpen = false;
        Refresh();
        Removed?.Invoke(window);
    }

    // ---- Order and edges ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Dragging a tab past the middle of another on its edge: it takes that one's place, which moves over to make room.
    /// Returns whether it moved.
    /// </summary>
    public bool MoveTo(ToolWindowViewModel window, ToolWindowViewModel to)
    {
        var edge = EdgeOf(window.Edge);
        var (from, index) = (edge.Windows.IndexOf(window), edge.Windows.IndexOf(to));
        if (from < 0 || index < 0 || from == index || to.Edge != window.Edge)
        {
            return false;
        }
        edge.Windows.RemoveAt(from);
        edge.Windows.Insert(index, window);
        Refresh();
        Save();
        return true;
    }

    /// <summary>A tab's <b>Move left</b> and <b>Move right</b> (up and down along the right edge): past the next tab that way.</summary>
    public void MoveBy(ToolWindowViewModel window, int offset)
    {
        var tabs = EdgeOf(window.Edge).Tabs;
        var from = tabs.IndexOf(window);
        var to = from + offset;
        if (from >= 0 && to >= 0 && to < tabs.Count)
        {
            MoveTo(window, tabs[to]);
        }
    }

    /// <summary>
    /// A tab dragged to the other edge, or its menu's <b>Move to the bottom</b> or <b>Move to the right</b>: it goes at
    /// <paramref name="index"/> among that edge's windows, or last, and is the tab it shows. An edge with no tabs docks
    /// as it takes its first.
    /// </summary>
    public void MoveToEdge(ToolWindowViewModel window, ToolEdge to, int? index = null)
    {
        if (!_windows.Contains(window))
        {
            return;
        }
        var (from, target) = (EdgeOf(window.Edge), EdgeOf(to));
        if (ReferenceEquals(from.SlidOut, window))
        {
            CancelSlideTimers();
            EndSlide();
        }
        if (ReferenceEquals(from, target))
        {
            return;
        }
        if (!target.Tabs.Any(t => !ReferenceEquals(t, window)))
        {
            target.IsAutoHide = false;
        }
        from.Windows.Remove(window);
        target.Windows.Insert(Math.Clamp(index ?? target.Windows.Count, 0, target.Windows.Count), window);
        window.Edge = to;
        window.IsOpen = true;
        window.OnEdgeStateChanged();
        target.Chosen = window;
        if (window.Kind.IsMultiple)
        {
            _kindEdges[window.Kind.Id] = to;
        }
        Refresh();
        Save();
    }

    // ---- Sizes -----------------------------------------------------------------------------------------------------

    /// <summary>Dragging an edge's inner side: at least its least, and the view keeps the panes their room. <see cref="SaveSize"/> keeps it.</summary>
    public void Resize(ToolEdge which, double size)
    {
        var edge = EdgeOf(which);
        edge.Size = Math.Max(size, edge.MinSize);
    }

    public void SaveSize() => Save();

    /// <summary>A double-click on an edge's side: its size at first.</summary>
    public void ResetSize(ToolEdge which)
    {
        var edge = EdgeOf(which);
        edge.Size = edge.DefaultSize;
        Save();
    }

    public void Dispose()
    {
        CancelSlideTimers();
        Unwatch();
    }
}
