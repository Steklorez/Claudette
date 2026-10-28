using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Claudette.App.ViewModels;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>
/// The tool windows' side of the shell's view (DESIGN.md §3, "Tool windows"): resizing a docked edge by its grip, keeping
/// the panes their least, the window slid out over them (the pointer, a press, the focus, Esc), and dragging a window's tab
/// along its edge or to the other one, marked as a tab dragged over the panes is.
/// </summary>
public partial class ShellView
{
    private const double ToolDragThreshold = 6;

    private ToolEdge? _resizingEdge;
    private Point _edgeResizeFrom;
    private double _edgeResizeStart;

    private ToolWindowViewModel? _pressedTool;
    private Point _toolPressedAt;
    private bool _draggingTool;
    private ToolDrop? _toolDrop;

    private ToolDockViewModel? Dock => ViewModel?.ToolWindows;

    /// <summary>From the constructor: the grips, the slides and the tool tabs' drags.</summary>
    private void WatchToolWindowsView()
    {
        WatchGrip(RightEdgeGrip, ToolEdge.Right);
        WatchGrip(BottomEdgeGrip, ToolEdge.Bottom);
        foreach (var slide in new[] { RightSlide, BottomSlide })
        {
            slide.PointerEntered += (_, _) => Dock?.PointInSlide(true);
            slide.PointerExited += (_, _) => Dock?.PointInSlide(false);
            slide.AddHandler(PointerPressedEvent, (_, _) => Dock?.PressInSlide(), RoutingStrategies.Tunnel, handledEventsToo: true);
            slide.GotFocus += (_, _) => Dock?.FocusInSlide(true);
            slide.LostFocus += (_, _) => Dock?.FocusInSlide(slide.IsKeyboardFocusWithin);
        }
        // A press anywhere else slides an auto-hidden window back; a press on a tool tab may start dragging it.
        DockArea.AddHandler(PointerPressedEvent, OnDockPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        DockArea.AddHandler(PointerMovedEvent, OnDockMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        DockArea.AddHandler(PointerReleasedEvent, OnDockReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // However wide or tall an edge was dragged, the panes keep their least.
        DockArea.SizeChanged += (_, e) =>
        {
            RightEdge.MaxWidth = Math.Max(ToolDockViewModel.MinRightWidth, e.NewSize.Width - ShellLayout.MinConversationWidth);
            RightSlide.MaxWidth = RightEdge.MaxWidth;
            BottomEdge.MaxHeight = Math.Max(ToolDockViewModel.MinBottomHeight, e.NewSize.Height - ToolDockViewModel.MinPanesHeight);
            BottomSlide.MaxHeight = BottomEdge.MaxHeight;
        };
    }

    /// <summary>The dock asks for the keyboard in a window it slid out on a click.</summary>
    private void OnToolFocusRequested(ToolWindowViewModel window)
    {
        var slide = window.Edge == ToolEdge.Right ? RightSlide : BottomSlide;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (slide.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible) is { } first)
            {
                first.Focus();
            }
        });
    }

    // ---- Resizing a docked edge ---------------------------------------------------------------------------------------

    private void WatchGrip(Border grip, ToolEdge edge)
    {
        grip.PointerPressed += (_, e) =>
        {
            if (Dock is not { } dock || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            {
                return;
            }
            _resizingEdge = edge;
            _edgeResizeFrom = e.GetPosition(DockArea);
            _edgeResizeStart = edge == ToolEdge.Right ? RightEdge.Bounds.Width : BottomEdge.Bounds.Height;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (_resizingEdge != edge || Dock is not { } dock)
            {
                return;
            }
            var at = e.GetPosition(DockArea);
            // The right edge widens as its grip goes left, the bottom grows as its grip goes up.
            var size = edge == ToolEdge.Right ? _edgeResizeStart + _edgeResizeFrom.X - at.X : _edgeResizeStart + _edgeResizeFrom.Y - at.Y;
            dock.Resize(edge, Math.Min(size, edge == ToolEdge.Right ? RightEdge.MaxWidth : BottomEdge.MaxHeight));
        };
        grip.PointerReleased += (_, e) => EndEdgeResize(e.Pointer);
        grip.PointerCaptureLost += (_, _) => EndEdgeResize(null);
        grip.DoubleTapped += (_, _) => Dock?.ResetSize(edge);
    }

    private void EndEdgeResize(IPointer? pointer)
    {
        if (_resizingEdge is null)
        {
            return;
        }
        _resizingEdge = null;
        pointer?.Capture(null);
        Dock?.SaveSize();
    }

    // ---- The slide, and dragging a tool tab ---------------------------------------------------------------------------

    /// <summary>The tool window whose tab is under a press: a docked group's or a strip's.</summary>
    private static ToolWindowViewModel? ToolTabAt(RoutedEventArgs e) =>
        (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { } button && button.Classes.Contains("tooltab")
            ? button.DataContext as ToolWindowViewModel
            : null;

    private void OnDockPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Dock is not { } dock)
        {
            return;
        }
        var source = e.Source as Visual;
        var inSlide = source is not null && (source.FindAncestorOfType<Border>(includeSelf: true) is { } border
            && (ReferenceEquals(border, RightSlide) || ReferenceEquals(border, BottomSlide) || border.GetVisualAncestors().Contains(RightSlide) || border.GetVisualAncestors().Contains(BottomSlide)));
        var tab = ToolTabAt(e);
        if (!inSlide && tab is null)
        {
            dock.PressOutside();
        }
        if (tab is not null && e.GetCurrentPoint(DockArea).Properties.IsLeftButtonPressed)
        {
            _pressedTool = tab;
            _toolPressedAt = e.GetPosition(DockArea);
            _draggingTool = false;
        }
    }

    private void OnDockMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedTool is not { } window || Dock is not { } dock)
        {
            return;
        }
        var at = e.GetPosition(DockArea);
        if (!_draggingTool)
        {
            if (Math.Abs(at.X - _toolPressedAt.X) < ToolDragThreshold && Math.Abs(at.Y - _toolPressedAt.Y) < ToolDragThreshold)
            {
                return;
            }
            _draggingTool = true;
            e.Pointer.Capture(DockArea);
        }
        // Along its own edge's tabs: it takes the place of the tab it's over, as the side panel's pages did.
        if (DockArea.InputHitTest(at) is Visual over && over.FindAncestorOfType<Button>(includeSelf: true) is { Classes: var classes, DataContext: ToolWindowViewModel to }
            && classes.Contains("tooltab") && !ReferenceEquals(to, window) && to.Edge == window.Edge)
        {
            dock.MoveTo(window, to);
            ShowDockDrop(null);
            return;
        }
        ShowDockDrop(dock.DropAt(window, at, Geometry()));
    }

    private void OnDockReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedTool is not { } window)
        {
            return;
        }
        if (_draggingTool)
        {
            if (_toolDrop is { } drop)
            {
                Dock?.Drop(window, drop);
            }
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        EndToolDrag();
    }

    /// <summary>A drag ended, or Esc cancelled it.</summary>
    private bool EndToolDrag()
    {
        var was = _draggingTool;
        _pressedTool = null;
        _draggingTool = false;
        ShowDockDrop(null);
        return was;
    }

    /// <summary>Where the dock's parts are, in the dock area's coordinates, for the drop marker.</summary>
    private ToolDockGeometry Geometry()
    {
        Rect? Of(Control control) => control.IsEffectivelyVisible && control.TranslatePoint(default, DockArea) is { } at ? new Rect(at, control.Bounds.Size) : null;
        var panes = Of(PanesArea) ?? new Rect(DockArea.Bounds.Size);
        var strips = DockArea.GetVisualChildren().OfType<ToolStripView>().ToList();
        return new ToolDockGeometry(panes, new Rect(DockArea.Bounds.Size), Of(RightEdge), Of(BottomEdge),
            strips.FirstOrDefault(s => s.DataContext is ToolEdgeViewModel { IsRight: true }) is { } right ? Of(right) : null,
            strips.FirstOrDefault(s => s.DataContext is ToolEdgeViewModel { IsBottom: true }) is { } bottom ? Of(bottom) : null);
    }

    private void ShowDockDrop(ToolDrop? drop)
    {
        _toolDrop = drop;
        DockDropLayer.IsVisible = drop is not null;
        if (drop is null)
        {
            return;
        }
        Canvas.SetLeft(DockDropTarget, drop.Area.X);
        Canvas.SetTop(DockDropTarget, drop.Area.Y);
        DockDropTarget.Width = drop.Area.Width;
        DockDropTarget.Height = drop.Area.Height;
        DockDropText.Text = drop.Label;
    }

    /// <summary>Esc: a tool tab's drag is cancelled, else a window slid out slides back. True when it did either.</summary>
    private bool EscapeToolWindows()
    {
        if (_draggingTool)
        {
            return EndToolDrag();
        }
        return Dock?.Escape() == true;
    }
}
