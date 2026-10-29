using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>
/// The tool window strip (DESIGN.md §3, "Tool windows"): its icons show and hide their windows by their commands, and
/// dragging one along the strip, above or under the divider or to the foot, moves it into that group, at the line that
/// marks where it would go.
/// </summary>
public partial class ToolStripView : UserControl
{
    /// <summary>An icon's geometry, by its key in the app's resources.</summary>
    public static readonly IValueConverter Icon = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryGetResource(key, null, out var found) == true ? found as Geometry : null);

    private const double DragThreshold = 6;

    private ToolDockViewModel? _dock;
    private ToolWindowViewModel? _pressed;
    private Point _pressedAt;
    private bool _dragging;
    private (ToolGroup Group, int Index)? _drop;

    public ToolStripView()
    {
        InitializeComponent();
        Strip.AddHandler(PointerPressedEvent, OnStripPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Strip.AddHandler(PointerMovedEvent, OnStripMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Strip.AddHandler(PointerReleasedEvent, OnStripReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Strip.PointerCaptureLost += (_, _) => CancelDrag();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_dock is not null)
        {
            _dock.PropertyChanged -= OnDockChanged;
        }
        _dock = DataContext as ToolDockViewModel;
        if (_dock is not null)
        {
            _dock.PropertyChanged += OnDockChanged;
        }
        ShowDivider();
    }

    private void OnDockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolDockViewModel.HasDivider))
        {
            ShowDivider();
        }
    }

    /// <summary>The line shows between icons above and under it, and while an icon is dragged, to drop under.</summary>
    private void ShowDivider() => Divider.IsVisible = _dragging || _dock?.HasDivider == true;

    private void OnIconContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: ToolWindowViewModel window } icon)
        {
            ToolWindowMenu.Open(window, icon, underTarget: false);
            e.Handled = true;
        }
    }

    // ---- Dragging an icon ------------------------------------------------------------------------------------------

    private void OnStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Strip).Properties.IsLeftButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: ToolWindowViewModel window } button
            && button.Classes.Contains("stripicon"))
        {
            _pressed = window;
            _pressedAt = e.GetPosition(Strip);
            _dragging = false;
        }
    }

    private void OnStripMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null)
        {
            return;
        }
        var at = e.GetPosition(Strip);
        if (!_dragging)
        {
            if (Math.Abs(at.Y - _pressedAt.Y) < DragThreshold && Math.Abs(at.X - _pressedAt.X) < DragThreshold)
            {
                return;
            }
            _dragging = true;
            ShowDivider();
            // The strip takes the pointer from the icon, which so doesn't click when it's let go.
            e.Pointer.Capture(Strip);
        }
        ShowDrop(at.Y);
    }

    private void OnStripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is not { } window)
        {
            return;
        }
        if (_dragging)
        {
            if (_drop is { } drop)
            {
                _dock?.MoveTo(window, drop.Group, drop.Index);
            }
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        CancelDrag();
    }

    /// <summary>A drag ended, or Esc cancelled it. True when one was under way.</summary>
    public bool CancelDrag()
    {
        var was = _dragging;
        _pressed = null;
        _dragging = false;
        _drop = null;
        DropLayer.IsVisible = false;
        ShowDivider();
        return was;
    }

    /// <summary>Marks the place nearest the pointer's height: before an icon of a group, after its last, or in a group with none.</summary>
    private void ShowDrop(double y)
    {
        var places = Places();
        if (places.Count == 0)
        {
            return;
        }
        var nearest = places.MinBy(p => Math.Abs(p.Y - y));
        _drop = (nearest.Group, nearest.Index);
        DropLayer.IsVisible = true;
        Canvas.SetLeft(DropLine, 4);
        Canvas.SetTop(DropLine, nearest.Y - DropLine.Height / 2);
        DropLine.Width = Math.Max(0, Strip.Bounds.Width - 8);
    }

    /// <summary>Every place an icon can go, with its height on the strip.</summary>
    private List<(ToolGroup Group, int Index, double Y)> Places()
    {
        var places = new List<(ToolGroup, int, double)>();
        void Of(ItemsControl icons, ToolGroup group, double whenEmpty)
        {
            var count = icons.ItemCount;
            if (count == 0)
            {
                places.Add((group, 0, whenEmpty));
                return;
            }
            var bottom = 0.0;
            for (var i = 0; i < count; i++)
            {
                if (icons.ContainerFromIndex(i) is not { } icon || icon.TranslatePoint(default, Strip) is not { } top)
                {
                    continue;
                }
                places.Add((group, i, top.Y - 2));
                bottom = top.Y + icon.Bounds.Height + 2;
            }
            places.Add((group, count, bottom));
        }
        double TopOf(Control control) => control.TranslatePoint(default, Strip)?.Y ?? 0;
        Of(TopIcons, ToolGroup.Top, TopOf(TopIcons) + 4);
        Of(SplitIcons, ToolGroup.Split, TopOf(Divider) + Divider.Bounds.Height + 6);
        Of(BottomIcons, ToolGroup.Bottom, Strip.Bounds.Height - 10);
        return places;
    }
}
