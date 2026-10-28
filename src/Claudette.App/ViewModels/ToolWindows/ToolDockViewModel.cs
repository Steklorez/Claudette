using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The tool windows (DESIGN.md §3, "Tool windows"), as Rider has them: one layout for the whole main window, outside the
/// panes, with a strip of icons at its right. The icons are in three groups, and one window of each shows at a time,
/// taking room beside the strip: the top group's at the right, the split group's under it, the bottom group's under the
/// panes. A window is Dock Pinned, staying until it's hidden, or Dock Unpinned, hiding once the user clicks elsewhere. The
/// windows that follow the session show the focused session tab's data. Partials: the session (<c>.Session</c>), what the
/// icons say beside their titles (<c>.Adornments</c>) and saving the layout (<c>.State</c>).
/// </summary>
public sealed partial class ToolDockViewModel : ViewModelBase, IDisposable
{
    /// <summary>The right side's width at first: the side panel's, which tool windows replaced.</summary>
    public const double DefaultRightWidth = 340;

    /// <summary>The least the right side can be dragged to: the side panel's.</summary>
    public const double MinRightWidth = 260;

    public const double DefaultBottomHeight = 240;

    public const double MinBottomHeight = 120;

    /// <summary>The panes keep at least this height above the bottom window.</summary>
    public const double MinPanesHeight = 200;

    /// <summary>The width of the strip of icons at the window's right.</summary>
    public const double StripWidth = 40;

    /// <summary>The top window's share of the right side's height at first, while the split group shows one too.</summary>
    public const double DefaultSplitShare = 0.5;

    /// <summary>The least share of the right side's height either of its windows keeps.</summary>
    public const double MinSplitShare = 0.15;

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly List<ToolWindowViewModel> _windows = [];

    /// <summary>Set once the layout is restored: until then, nothing is worked out or saved.</summary>
    private readonly bool _ready;

    /// <param name="saveState">Saves <paramref name="state"/> once the layout changes.</param>
    internal ToolDockViewModel(AppState state, Action saveState)
    {
        _state = state;
        _saveState = saveState;
        Top = new ToolGroupViewModel(this, ToolGroup.Top);
        Split = new ToolGroupViewModel(this, ToolGroup.Split);
        Bottom = new ToolGroupViewModel(this, ToolGroup.Bottom);
        foreach (var kind in ToolWindowKinds.All)
        {
            _windows.Add(new ToolWindowViewModel(this, kind));
        }
        Restore();
        _ready = true;
        Refresh();
    }

    /// <summary>The right side's top: the group above the strip's divider.</summary>
    public ToolGroupViewModel Top { get; }

    /// <summary>The right side's bottom, which splits it with the top: the group under the strip's divider.</summary>
    public ToolGroupViewModel Split { get; }

    /// <summary>Under the panes: the group at the strip's foot.</summary>
    public ToolGroupViewModel Bottom { get; }

    public IReadOnlyList<ToolGroupViewModel> Groups => [Top, Split, Bottom];

    /// <summary>Every window, with something to show or not.</summary>
    public IReadOnlyList<ToolWindowViewModel> Windows => _windows;

    public ToolGroupViewModel GroupOf(ToolGroup group) => group switch
    {
        ToolGroup.Top => Top,
        ToolGroup.Split => Split,
        _ => Bottom,
    };

    /// <summary>The window of <paramref name="kind"/>.</summary>
    public ToolWindowViewModel For(ToolWindowKind kind) => _windows.First(w => w.Kind == kind);

    private ToolWindowViewModel? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    // ---- What's on screen ----------------------------------------------------------------------------------------

    /// <summary>The strip has an icon.</summary>
    [ObservableProperty]
    public partial bool HasStrip { get; private set; }

    /// <summary>The strip has icons both above its divider and under it, so the divider shows.</summary>
    [ObservableProperty]
    public partial bool HasDivider { get; private set; }

    /// <summary>A window shows at the right: the top group's, the split group's, or both.</summary>
    [ObservableProperty]
    public partial bool IsRightShowing { get; private set; }

    /// <summary>The right side's width, as the user dragged it.</summary>
    [ObservableProperty]
    public partial double RightWidth { get; private set; } = DefaultRightWidth;

    /// <summary>The bottom's height, as the user dragged it.</summary>
    [ObservableProperty]
    public partial double BottomHeight { get; private set; } = DefaultBottomHeight;

    /// <summary>The top window's share of the right side's height, while the split group shows one too.</summary>
    [ObservableProperty]
    public partial double SplitShare { get; private set; } = DefaultSplitShare;

    /// <summary>
    /// The least room the tool windows take at the right, which the sidebar and the panes leave them (DESIGN.md §4,
    /// "Sidebar"; §18, "Split panes"): the strip, and the right side's least width while a window shows there.
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

    /// <summary>The icons follow the windows, the groups what they show, and every window hears what changed.</summary>
    internal void Refresh()
    {
        if (!_ready)
        {
            return;
        }
        foreach (var group in Groups)
        {
            group.Refresh();
        }
        HasStrip = Groups.Any(g => g.HasIcons);
        HasDivider = Top.HasIcons && Split.HasIcons;
        IsRightShowing = Top.IsShowing || Split.IsShowing;
        RightRoom = (HasStrip ? StripWidth : 0) + (IsRightShowing ? MinRightWidth : 0);
        ShowingChanged?.Invoke();
    }

    // ---- Showing and hiding ----------------------------------------------------------------------------------------

    /// <summary>
    /// A window wanted from a button, a menu or the command palette: it shows, in place of its group's other. Shown by
    /// <see cref="Toggle"/>, a click on its icon, it takes the keyboard too.
    /// </summary>
    public void Show(ToolWindowKind kind) => Show(For(kind));

    /// <inheritdoc cref="Show(ToolWindowKind)"/>
    public void Show(ToolWindowViewModel window)
    {
        var group = GroupOf(window.Group);
        group.Chosen = window;
        Refresh();
        Save();
    }

    /// <summary>A click on its icon: hidden, it shows and takes the keyboard; showing, it hides.</summary>
    public void Toggle(ToolWindowViewModel window)
    {
        if (window.IsShowing)
        {
            Hide(window);
            return;
        }
        Show(window);
        if (window.IsShowing)
        {
            FocusRequested?.Invoke(window);
        }
    }

    /// <summary>Its <b>−</b>, its menu's <b>Hide</b>, or for one unpinned, a click elsewhere: it goes, and its group shows nothing.</summary>
    public void Hide(ToolWindowViewModel window)
    {
        var group = GroupOf(window.Group);
        if (!ReferenceEquals(group.Chosen, window))
        {
            return;
        }
        group.Chosen = null;
        Refresh();
        Save();
    }

    /// <summary>A window shown by a click on its icon: the view puts the keyboard focus in it.</summary>
    public event Action<ToolWindowViewModel>? FocusRequested;

    /// <summary>Its menu's <b>Dock Pinned</b> and <b>Dock Unpinned</b>.</summary>
    public void SetPinned(ToolWindowViewModel window, bool pinned)
    {
        if (window.IsPinned == pinned)
        {
            return;
        }
        window.IsPinned = pinned;
        Save();
    }

    /// <summary>
    /// The pointer was pressed in the main window: an unpinned window showing hides unless the press was in it
    /// (<paramref name="inside"/>), or on the strip, whose icons do their own showing and hiding.
    /// </summary>
    public void PressAt(ToolWindowViewModel? inside, bool onStrip)
    {
        if (onStrip)
        {
            return;
        }
        HideUnpinned(except: inside);
    }

    /// <summary>Esc: an unpinned window showing hides. False when none did.</summary>
    public bool Escape() => HideUnpinned(except: null);

    private bool HideUnpinned(ToolWindowViewModel? except)
    {
        var hid = false;
        foreach (var group in Groups)
        {
            if (group.Showing is { IsPinned: false } showing && !ReferenceEquals(showing, except))
            {
                group.Chosen = null;
                hid = true;
            }
        }
        if (hid)
        {
            Refresh();
            Save();
        }
        return hid;
    }

    // ---- Moving ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// An icon dragged along the strip, or its menu's <b>Move to</b>: it goes into <paramref name="to"/>, before the icon now
    /// at <paramref name="index"/> among its icons, or last. Showing, it shows there in place of that group's window.
    /// </summary>
    public void MoveTo(ToolWindowViewModel window, ToolGroup to, int? index = null)
    {
        var (from, target) = (GroupOf(window.Group), GroupOf(to));
        var showing = window.IsShowing;
        // The icon it goes before: the one at the index, or past itself, the next.
        var before = index is { } at ? target.Icons.Skip(Math.Max(at, 0)).FirstOrDefault(i => !ReferenceEquals(i, window)) : null;
        if (ReferenceEquals(from.Chosen, window))
        {
            from.Chosen = null;
        }
        from.Windows.Remove(window);
        target.Windows.Insert(before is null ? target.Windows.Count : target.Windows.IndexOf(before), window);
        window.Group = to;
        if (showing)
        {
            target.Chosen = window;
        }
        Refresh();
        Save();
    }

    // ---- Sizes -----------------------------------------------------------------------------------------------------

    /// <summary>Dragging the right side's inner edge: at least its least, and the view keeps the panes their room. <see cref="SaveSize"/> keeps it.</summary>
    public void ResizeRight(double width) => RightWidth = Math.Max(width, MinRightWidth);

    /// <summary>Dragging the bottom's top edge.</summary>
    public void ResizeBottom(double height) => BottomHeight = Math.Max(height, MinBottomHeight);

    /// <summary>Dragging the line between the right side's two windows: the top's share of its height.</summary>
    public void ResizeSplit(double share) => SplitShare = Math.Clamp(share, MinSplitShare, 1 - MinSplitShare);

    public void SaveSize() => Save();

    /// <summary>A double-click on the right side's edge: its width at first.</summary>
    public void ResetRightWidth()
    {
        RightWidth = DefaultRightWidth;
        Save();
    }

    /// <summary>A double-click on the bottom's edge: its height at first.</summary>
    public void ResetBottomHeight()
    {
        BottomHeight = DefaultBottomHeight;
        Save();
    }

    /// <summary>A double-click on the line between the right side's windows: half each.</summary>
    public void ResetSplit()
    {
        SplitShare = DefaultSplitShare;
        Save();
    }

    public void Dispose() => Unwatch();
}
