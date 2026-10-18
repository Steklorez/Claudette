using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>
/// An image file clicked in a reply, rendered in the image window, and an animated GIF playing there (DESIGN.md §18,
/// "Opening images").
/// </summary>
public class ImageOpeningUiTests
{
    [AvaloniaFact]
    public async Task An_animated_GIF_a_reply_links_to_plays_in_the_window_and_Pause_stops_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await File.WriteAllBytesAsync(Path.Combine(h.WorkFolder, "loop.gif"), TestImages.Gif([10, 10, 10]), TestContext.Current.CancellationToken);

        // Every frame the window shows, from the moment it opens: the image as XAML binds it is not one of them.
        var frames = new List<object?>();
        Image? picture = null;
        var show = h.Shell.ShowImageWindow!;
        h.Shell.ShowImageWindow = v =>
        {
            show(v);
            picture = window.OwnedWindows.OfType<ImageWindow>().Single().GetVisualDescendants().OfType<Image>().Single(i => i.Name == "Picture");
            picture.PropertyChanged += (_, e) =>
            {
                if (e.Property == Image.SourceProperty)
                {
                    frames.Add(e.NewValue);
                }
            };
        };

        await tab.OpenLinkCommand.ExecuteAsync(new LinkClickedEventArgs(MarkdownTextBlock.LinkClickEvent, new object(), new Uri("loop.gif", UriKind.Relative)));
        var viewer = Assert.Single(window.OwnedWindows.OfType<ImageWindow>());
        await UiText.SettleUntilAsync(viewer, () => frames.Count == 1, "the first frame");
        Assert.IsType<Bitmap>(frames[0]);

        var shown = UiText.Describe(viewer);
        Assert.Contains("Pause", shown, StringComparison.Ordinal);
        Assert.Contains("loop.gif · GIF", shown, StringComparison.Ordinal);
        Assert.Contains("3 frames", shown, StringComparison.Ordinal);

        // Each frame is decoded as it comes, in place of the one before.
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await UiText.SettleUntilAsync(viewer, () => frames.Count == 2, "the second frame");
        Assert.NotSame(frames[0], frames[1]);
        Assert.Same(frames[1], picture!.Source);

        var pause = viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Pause");
        pause.Command!.Execute(null);
        UiText.Settle(viewer);
        Assert.Equal("Play", pause.Content);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        UiText.Settle(viewer);
        Assert.Equal(2, frames.Count);

        viewer.Close();
        UiText.Settle(window);
        Assert.Null(picture.Source);
    }
}
