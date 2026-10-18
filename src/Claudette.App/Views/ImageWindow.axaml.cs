using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// An image full size in a window of its own (DESIGN.md §18, "Images from Claude's tools", "Opening images"). An animated
/// one shows each frame as the viewer moves on to it, decoded off the UI thread.
/// </summary>
public partial class ImageWindow : Window
{
    private ImageViewerViewModel? _viewer;

    /// <summary>The frame showing, which this window made and disposes.</summary>
    private Bitmap? _frame;

    private bool _rendering;

    /// <summary>The viewer moved on while a frame was decoding: decode the one it's on now, next.</summary>
    private bool _behind;

    private bool _closed;

    public ImageWindow() => InitializeComponent();

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
        if (_viewer is { IsAnimated: true })
        {
            _viewer.PropertyChanged += OnViewerChanged;
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
    /// It opens at the image's size, up to its largest; from then on it can be made any size, the image shrinking to
    /// fit but never growing past its own size.
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
        }
    }
}
