using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// One tool window (DESIGN.md §3, "Tool windows"): its icon on the strip, in a group, whether it shows, its view mode and
/// what it shows. The dock keeps its place; its kind says what it is.
/// </summary>
public sealed partial class ToolWindowViewModel : ViewModelBase
{
    private readonly ToolDockViewModel _dock;

    internal ToolWindowViewModel(ToolDockViewModel dock, ToolWindowKind kind)
    {
        _dock = dock;
        Kind = kind;
        Group = kind.DefaultGroup;
        // A kind that shows only when the session has something for it waits to be told it has; Games and Find wait for
        // the shell.
        IsAvailable = kind.ShowsWhen is null && kind.FollowsSession;
    }

    public ToolWindowKind Kind { get; }

    /// <summary>Its id, which its place is saved by: its kind's.</summary>
    public string Id => Kind.Id;

    /// <summary>What its header and its icon's tip say, such as <i>Changed files</i>.</summary>
    public string Title => Kind.Title;

    /// <summary>
    /// What its view shows: for a kind that follows the session, the session tab whose data it shows (null with no session
    /// tab); for others, what the shell gave it.
    /// </summary>
    [ObservableProperty]
    public partial object? Content { get; set; }

    /// <summary>Its group on the strip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInTop), nameof(IsInSplit), nameof(IsInBottom))]
    public partial ToolGroup Group { get; internal set; }

    public bool IsInTop => Group == ToolGroup.Top;

    public bool IsInSplit => Group == ToolGroup.Split;

    public bool IsInBottom => Group == ToolGroup.Bottom;

    /// <summary>
    /// It has something to show: for a kind that follows the session, as <see cref="ToolWindowKind.ShowsWhen"/> says of the
    /// session (the dock keeps it); for others, as the shell says. Its icon hides the rest of the time.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    partial void OnIsAvailableChanged(bool value) => _dock.Refresh();

    /// <summary>On screen, beside the strip: the window of its group showing.</summary>
    [ObservableProperty]
    public partial bool IsShowing { get; internal set; }

    /// <summary>
    /// Dock Pinned, as Rider calls it: it stays until it's hidden. Unpinned, it hides once the user clicks outside it, or
    /// presses Esc.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnpinned))]
    public partial bool IsPinned { get; internal set; } = true;

    public bool IsUnpinned => !IsPinned;

    // ---- Beside its icon, as the side panel's tabs had (DESIGN.md §3, "Tool windows") ------------------------------

    /// <summary>Its work is running: a subagent, or a project's job. A dot on its icon in the busy color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial bool IsBusy { get; internal set; }

    /// <summary>Something needs looking at, such as an MCP server that failed. A dot on its icon in the warning color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial bool HasWarning { get; internal set; }

    /// <summary>A dot on its icon: busy, or a warning.</summary>
    public bool HasDot => IsBusy || HasWarning;

    /// <summary>What it holds now, such as Tasks' progress, <i>To-do · 2 of 5 done</i>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconTip))]
    public partial string? Tip { get; internal set; }

    /// <summary>Its icon's tip: its title, then what it holds now when that says more, as <i>Tasks · To-do · 2 of 5 done</i>.</summary>
    public string IconTip => Tip is { Length: > 0 } tip && tip != Title ? $"{Title} · {tip}" : Title;

    // ---- What the user does ------------------------------------------------------------------------------------------

    /// <summary>Its icon clicked: it shows, in place of its group's other, and takes the keyboard; showing, it hides.</summary>
    [RelayCommand]
    private void Toggle() => _dock.Toggle(this);

    /// <summary>The <b>−</b> on its header, or its menu's <b>Hide</b>.</summary>
    [RelayCommand]
    private void Hide() => _dock.Hide(this);

    /// <summary>Its menu's <b>Dock Pinned</b>.</summary>
    [RelayCommand]
    private void Pin() => _dock.SetPinned(this, true);

    /// <summary>Its menu's <b>Dock Unpinned</b>.</summary>
    [RelayCommand]
    private void Unpin() => _dock.SetPinned(this, false);

    /// <summary>Its menu's <b>Move to</b> → <b>Right top</b>.</summary>
    [RelayCommand]
    private void MoveToTop() => _dock.MoveTo(this, ToolGroup.Top);

    /// <summary>Its menu's <b>Move to</b> → <b>Right bottom</b>, splitting the right side.</summary>
    [RelayCommand]
    private void MoveToSplit() => _dock.MoveTo(this, ToolGroup.Split);

    /// <summary>Its menu's <b>Move to</b> → <b>Bottom</b>, under the panes.</summary>
    [RelayCommand]
    private void MoveToBottom() => _dock.MoveTo(this, ToolGroup.Bottom);

    public override string ToString() => Title;
}
