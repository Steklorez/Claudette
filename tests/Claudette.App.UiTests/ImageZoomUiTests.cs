using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.Core.Protocol;

namespace Claudette.App.UiTests;

/// <summary>
/// The image window zoomed by keyboard and the mouse wheel, and dragged about once it's larger than the window
/// (DESIGN.md §18, "Images from Claude's tools", "Zoom").
/// </summary>
public class ImageZoomUiTests
{
    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        UiText.Settle(window);
    }

    private static void Drag(Window window, Point from, Point to, MouseButton button)
    {
        window.MouseMove(from);
        window.MouseDown(from, button);
        window.MouseMove(to);
        window.MouseUp(to, button);
        UiText.Settle(window);
    }

    /// <summary>The point of the image at <paramref name="at"/> in the scroller, as a share of its width and height.</summary>
    private static Point ShareAt(Image picture, ScrollViewer scroller, Point at)
    {
        var corner = picture.TranslatePoint(default, scroller)!.Value;
        return new Point((at.X - corner.X) / picture.Bounds.Width, (at.Y - corner.Y) / picture.Bounds.Height);
    }

    /// <summary>
    /// The size the picture draws its image at, which can be smaller than the size it's laid out at: headless drawing
    /// draws nothing to look at.
    /// </summary>
    private static Size Drawn(Image picture) =>
        picture.Stretch.CalculateSize(picture.Bounds.Size, picture.Source!.Size, picture.StretchDirection);

    /// <summary>Where the point of the image at <paramref name="share"/> of its width and height is, in the scroller.</summary>
    private static Point PointAt(Image picture, ScrollViewer scroller, Point share)
    {
        var corner = picture.TranslatePoint(default, scroller)!.Value;
        return new Point(corner.X + share.X * picture.Bounds.Width, corner.Y + share.Y * picture.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task Plus_and_the_wheel_zoom_in_about_the_middle_and_the_pointer_a_drag_moves_it_and_0_fits_it_again()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var owner = UiText.Show(new Panel());
        using var viewer = new ImageViewerViewModel(new ToolImage(new MessageImage("image/png", TestImages.Png), "Read", "shot.png", "shot.png", null, null), h.Services);
        // As if the user had made it this size: a headless window keeps the size it opens at.
        var window = new ImageWindow { DataContext = viewer, SizeToContent = SizeToContent.Manual, Width = 800, Height = 600 };
        window.ShowBeside(owner);
        var picture = window.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "Picture");
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Scroller");
        // Headless drawing decodes no pixels, so the window is given a bitmap of the size to zoom.
        using var bitmap = new WriteableBitmap(new PixelSize(2000, 1500), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        picture.Source = bitmap;
        UiText.Settle(window);

        // It fits the window, and the header says how large it is.
        var fit = picture.Bounds.Size;
        Assert.True(viewer.IsFit);
        Assert.Equal(fit.Width / 20, viewer.FitPercent, 3);
        Assert.Contains(viewer.ZoomText, UiText.Describe(window), StringComparison.Ordinal);
        Assert.Null(picture.Cursor);

        // + zooms in a step, about the middle.
        Press(window, PhysicalKey.Equal, RawInputModifiers.Shift);
        Assert.Equal(50, viewer.Zoom);
        Assert.Equal(new Size(1000, 750), picture.Bounds.Size);
        Assert.Equal(picture.Bounds.Size, Drawn(picture));
        var middle = new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2);
        // Within a pixel: layout rounds to whole ones.
        var center = PointAt(picture, scroller, new Point(0.5, 0.5));
        Assert.Equal(middle.X, center.X, 1.0);
        Assert.Equal(middle.Y, center.Y, 1.0);
        Assert.Contains("50%", UiText.Describe(window), StringComparison.Ordinal);
        Assert.NotNull(picture.Cursor);

        // A notch of the wheel zooms in another, keeping what was under the pointer there.
        var pointer = new Point(100, 80);
        var under = ShareAt(picture, scroller, pointer);
        window.MouseWheel(scroller.TranslatePoint(pointer, window)!.Value, new Vector(0, 1));
        UiText.Settle(window);
        Assert.Equal(67, viewer.Zoom);
        var still = PointAt(picture, scroller, under);
        Assert.Equal(pointer.X, still.X, 1.0);
        Assert.Equal(pointer.Y, still.Y, 1.0);

        // Larger than the window, it drags about with the left button, and the middle one.
        var offset = scroller.Offset;
        var from = scroller.TranslatePoint(pointer, window)!.Value;
        Drag(window, from, from - new Point(120, 60), MouseButton.Left);
        Assert.Equal(offset + new Vector(120, 60), scroller.Offset);
        Drag(window, from, from + new Point(20, 10), MouseButton.Middle);
        Assert.Equal(offset + new Vector(100, 50), scroller.Offset);

        // Space held over it, for dragging, doesn't press the button with the focus.
        var zoomIn = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Zoom in");
        zoomIn.Focus();
        window.MouseMove(from);
        Press(window, PhysicalKey.Space);
        Assert.Equal(67, viewer.Zoom);

        // Larger than its own size, it's drawn so; the number pad's + and the main keyboard's − step too.
        while (viewer.Zoom < 200)
        {
            Press(window, PhysicalKey.NumPadAdd);
        }
        Assert.Equal(new Size(4000, 3000), picture.Bounds.Size);
        Assert.Equal(picture.Bounds.Size, Drawn(picture));
        Press(window, PhysicalKey.Minus);
        Assert.Equal(150, viewer.Zoom);

        // 0 fits it in the window again.
        Press(window, PhysicalKey.Digit0);
        Assert.True(viewer.IsFit);
        Assert.Equal(fit, picture.Bounds.Size);
        Assert.Equal(default, scroller.Offset);
        Assert.Null(picture.Cursor);

        window.Close();
        UiText.Settle(owner);
    }
}
