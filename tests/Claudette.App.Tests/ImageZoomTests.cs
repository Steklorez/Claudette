using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Protocol;

namespace Claudette.App.Tests;

/// <summary>
/// The image window's zoom (DESIGN.md §18, "Images from Claude's tools", "Zoom"): it opens fitting the window, and
/// <b>+</b> and <b>−</b> step on from the percentage it reads, up to 1600% and down to 10%.
/// </summary>
public class ImageZoomTests
{
    private static ImageViewerViewModel Viewer(TabTestHarness h) =>
        new(new ToolImage(new MessageImage("image/png", TestImages.Png), "Read", "shot.png", "shot.png", null, null), h.Services);

    [Fact]
    public async Task It_opens_fitting_the_window_and_steps_on_from_the_percentage_it_fits_at()
    {
        await using var h = new TabTestHarness();
        using var viewer = Viewer(h);
        Assert.True(viewer is { IsFit: true, ZoomText: "100%" });
        Assert.False(viewer.FitCommand.CanExecute(null));

        // The window says what it fits at, as it changes size.
        viewer.FitPercent = 45.4;
        Assert.Equal("45%", viewer.ZoomText);

        viewer.ZoomInCommand.Execute(null);
        Assert.True(viewer is { IsFit: false, Zoom: 50, ZoomText: "50%" });
        Assert.True(viewer.FitCommand.CanExecute(null));
        viewer.ZoomInCommand.Execute(null);
        Assert.Equal(67, viewer.Zoom);
        viewer.ZoomOutCommand.Execute(null);
        Assert.Equal(50, viewer.Zoom);

        // Zoomed, the window's size changes nothing.
        viewer.FitPercent = 80;
        Assert.Equal("50%", viewer.ZoomText);

        viewer.FitCommand.Execute(null);
        Assert.True(viewer is { IsFit: true, Zoom: null, ZoomText: "80%" });
        viewer.ZoomOutCommand.Execute(null);
        Assert.Equal(75, viewer.Zoom);
    }

    [Fact]
    public async Task A_fit_that_reads_as_a_step_steps_past_it()
    {
        await using var h = new TabTestHarness();
        using var viewer = Viewer(h);

        viewer.FitPercent = 49.6;
        Assert.Equal("50%", viewer.ZoomText);
        viewer.ZoomInCommand.Execute(null);
        Assert.Equal(67, viewer.Zoom);

        viewer.FitCommand.Execute(null);
        viewer.ZoomOutCommand.Execute(null);
        Assert.Equal(33, viewer.Zoom);
    }

    [Fact]
    public async Task It_zooms_from_10_to_1600_percent_and_no_further()
    {
        await using var h = new TabTestHarness();
        using var viewer = Viewer(h);

        while (viewer.ZoomInCommand.CanExecute(null))
        {
            viewer.ZoomInCommand.Execute(null);
        }
        Assert.Equal(1600, viewer.Zoom);
        while (viewer.ZoomOutCommand.CanExecute(null))
        {
            viewer.ZoomOutCommand.Execute(null);
        }
        Assert.Equal(10, viewer.Zoom);

        // An image so large it fits at less than the smallest step zooms in to it, and no further out.
        viewer.FitCommand.Execute(null);
        viewer.FitPercent = 4;
        Assert.False(viewer.ZoomOutCommand.CanExecute(null));
        viewer.ZoomInCommand.Execute(null);
        Assert.Equal(10, viewer.Zoom);
    }
}
