using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Claudette.App.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.Views;

/// <summary>
/// An image full size in a window of its own (DESIGN.md §18, "Images from Claude's tools", "Opening images"). An animated
/// one shows each frame as the viewer moves on to it, decoded off the UI thread. The mouse wheel over it zooms in and
/// out about the pointer, as +, − and 0 do about the middle, and once it's larger than the window it drags about with
/// the left button (Space held or not) or the middle one.
/// </summary>
public partial class ImageWindow : Window
{
    private static readonly Cursor Move = new(StandardCursorType.SizeAll);

    private ImageViewerViewModel? _viewer;

    /// <summary>The frame showing, which this window made and disposes.</summary>
    private Bitmap? _frame;

    private bool _rendering;

    /// <summary>The viewer moved on while a frame was decoding: decode the one it's on now, next.</summary>
    private bool _behind;

    private bool _closed;

    /// <summary>How far the wheel has turned towards the next step: a touchpad turns it a little at a time.</summary>
    private double _wheel;

    /// <summary>Where the wheel turned, in the scroller: the zoom keeps that point of the image under the pointer.</summary>
    private Point? _zoomAt;

    /// <summary>Where a drag started, in the scroller, and how far it was scrolled then.</summary>
    private (Point Pointer, Vector Offset)? _drag;

    public ImageWindow()
    {
        InitializeComponent();
        // Before the scroller scrolls with it.
        Scroller.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        Scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
            {
                Picture.Cursor = CanDrag ? Move : null;
            }
        };
        Picture.SizeChanged += (_, _) => MeasureFit();
        Scroller.PointerPressed += OnScrollerPressed;
        Scroller.PointerMoved += OnScrollerMoved;
        Scroller.PointerReleased += (_, e) => EndDrag(e.Pointer);
        Scroller.PointerCaptureLost += (_, _) => _drag = null;
        // Before a button with the focus takes it.
        AddHandler(KeyDownEvent, OnSpace, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnSpace, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Shows <paramref name="viewer"/> in a window beside <paramref name="owner"/> (or on its own without one), which
    /// disposes the viewer when it closes.
    /// </summary>
    public static void Open(ImageViewerViewModel viewer, Window? owner)
    {
        var window = new ImageWindow { DataContext = viewer };
        window.Closed += (_, _) => viewer.Dispose();
        if (owner is not null)
        {
            window.ShowBeside(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// Opens it beside <paramref name="owner"/>, no larger than 90% of the screen that's on: a 1080p laptop at 150% has
    /// only 720 pixels of height to give it.
    /// </summary>
    public void ShowBeside(Window owner)
    {
        if (owner.Screens.ScreenFromWindow(owner) is { } screen)
        {
            var area = screen.WorkingArea.ToRect(screen.Scaling);
            MaxWidth = Math.Max(MinWidth, Math.Min(MaxWidth, area.Width * 0.9));
            MaxHeight = Math.Max(MinHeight, Math.Min(MaxHeight, area.Height * 0.9));
        }
        Show(owner);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewer is not null)
        {
            _viewer.PropertyChanged -= OnViewerChanged;
        }
        _viewer = DataContext as ImageViewerViewModel;
        if (_viewer is null)
        {
            return;
        }
        _viewer.PropertyChanged += OnViewerChanged;
        if (_viewer.IsAnimated)
        {
            // Avalonia shows a GIF's first frame by itself, but not every animated WebP's.
            RenderFrame();
        }
    }

    private void OnViewerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageViewerViewModel.Frame))
        {
            RenderFrame();
        }
        else if (e.PropertyName == nameof(ImageViewerViewModel.Zoom))
        {
            ShowZoom();
        }
    }

    /// <summary>
    /// Decodes the viewer's frame and shows it, one at a time: a frame asked for meanwhile is decoded next, and any it
    /// moved past are skipped, so a slow decode drops frames rather than falling behind.
    /// </summary>
    private async void RenderFrame()
    {
        if (_rendering)
        {
            _behind = true;
            return;
        }
        _rendering = true;
        try
        {
            do
            {
                _behind = false;
                if (_closed || _viewer?.Animation is not { } animation)
                {
                    return;
                }
                var index = _viewer.Frame;
                var bitmap = await Task.Run(() => animation.Render(index));
                if (bitmap is null)
                {
                    continue;
                }
                if (_closed)
                {
                    bitmap.Dispose();
                    return;
                }
                var shown = _frame;
                _frame = bitmap;
                Picture.Source = bitmap;
                shown?.Dispose();
            }
            while (_behind);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>
    /// The image at the viewer's zoom: fitting the window, or at its step's size, keeping the point of it that was
    /// under the pointer (or in the middle) where it was.
    /// </summary>
    private void ShowZoom()
    {
        if (_viewer is null || Picture.Source is not { Size: { Width: > 0, Height: > 0 } size })
        {
            return;
        }
        if (_viewer.Zoom is not { } percent)
        {
            Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Picture.Stretch = Stretch.Uniform;
            Picture.StretchDirection = StretchDirection.DownOnly;
            Picture.Width = double.NaN;
            Picture.Height = double.NaN;
            return;
        }

        // The point that stays put, and which point of the image is there, as a share of its width and height.
        var at = _zoomAt ?? new Point(Scroller.Bounds.Width / 2, Scroller.Bounds.Height / 2);
        var shown = Picture.Bounds.Size;
        var corner = Picture.TranslatePoint(default, Scroller) ?? default;
        var share = shown is { Width: > 0, Height: > 0 }
            ? new Point(Math.Clamp((at.X - corner.X) / shown.Width, 0, 1), Math.Clamp((at.Y - corner.Y) / shown.Height, 0, 1))
            : new Point(0.5, 0.5);

        var scale = percent / 100.0;
        Picture.Stretch = Stretch.Fill;
        // Drawn larger than it is, too, not only laid out so.
        Picture.StretchDirection = StretchDirection.Both;
        Picture.Width = size.Width * scale;
        Picture.Height = size.Height * scale;
        Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Scroller.UpdateLayout();

        var now = Picture.TranslatePoint(default, Scroller) ?? default;
        Scroller.Offset = new Vector(
            Scroller.Offset.X + now.X - (at.X - share.X * Picture.Width),
            Scroller.Offset.Y + now.Y - (at.Y - share.Y * Picture.Height));
    }

    /// <summary>Fitting, the viewer hears the percentage it fits at, which <b>+</b> and <b>−</b> step on from.</summary>
    private void MeasureFit()
    {
        if (_viewer is { IsFit: true } && Picture.Source is { Size.Width: > 0 } source && Picture.Bounds.Width > 0)
        {
            _viewer.FitPercent = Math.Min(100, Picture.Bounds.Width / source.Size.Width * 100);
        }
    }

    /// <summary>Each notch of the wheel is a step, up to zoom in and down to zoom out, about the pointer.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (_viewer is null)
        {
            return;
        }
        if (Math.Sign(e.Delta.Y) != Math.Sign(_wheel))
        {
            _wheel = 0;
        }
        _wheel += e.Delta.Y;
        _zoomAt = e.GetPosition(Scroller);
        try
        {
            while (Math.Abs(_wheel) >= 1)
            {
                if (!Run(_wheel > 0 ? _viewer.ZoomInCommand : _viewer.ZoomOutCommand))
                {
                    _wheel = 0;
                    break;
                }
                _wheel -= Math.Sign(_wheel);
            }
        }
        finally
        {
            _zoomAt = null;
        }
    }

    private static bool Run(IRelayCommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }
        command.Execute(null);
        return true;
    }

    /// <summary>It's larger than the window, so there's more of it to drag into view.</summary>
    private bool CanDrag => Scroller.Extent.Width > Scroller.Viewport.Width + 0.5 || Scroller.Extent.Height > Scroller.Viewport.Height + 0.5;

    /// <summary>The left button drags it, with Space held or without, and the middle button too, as in other image apps.</summary>
    private void OnScrollerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(Scroller).Properties;
        if (!CanDrag || !(properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed))
        {
            return;
        }
        _drag = (e.GetPosition(Scroller), Scroller.Offset);
        e.Pointer.Capture(Scroller);
        e.Handled = true;
    }

    /// <summary>Space held over the image is for dragging it, so it doesn't press the button with the focus as well.</summary>
    private void OnSpace(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && (Scroller.IsPointerOver || _drag is not null))
        {
            e.Handled = true;
        }
    }

    private void OnScrollerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag)
        {
            return;
        }
        var moved = e.GetPosition(Scroller) - drag.Pointer;
        Scroller.Offset = new Vector(drag.Offset.X - moved.X, drag.Offset.Y - moved.Y);
    }

    private void EndDrag(IPointer pointer)
    {
        if (_drag is null)
        {
            return;
        }
        _drag = null;
        pointer.Capture(null);
    }

    /// <summary>
    /// It opens at the image's size, up to its largest; from then on it can be made any size, the image shrinking to
    /// fit but never growing past its own size until it's zoomed.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SizeToContent = SizeToContent.Manual;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_viewer is not null)
        {
            _viewer.PropertyChanged -= OnViewerChanged;
        }
        base.OnClosed(e);
        if (_frame is not null)
        {
            Picture.Source = null;
            _frame.Dispose();
            _frame = null;
        }
    }

    /// <summary>Esc closes it; +, − and 0 zoom in, out and back to fitting, with Ctrl or Cmd too, as the main window's zoom is.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (_viewer is null || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }
        // + is Shift and = on many keyboards, so = zooms in too.
        IRelayCommand? command = e.Key switch
        {
            Key.OemPlus or Key.Add => _viewer.ZoomInCommand,
            Key.OemMinus or Key.Subtract => _viewer.ZoomOutCommand,
            Key.D0 or Key.NumPad0 => _viewer.FitCommand,
            _ => null,
        };
        if (command is not null)
        {
            Run(command);
            e.Handled = true;
        }
    }
}
