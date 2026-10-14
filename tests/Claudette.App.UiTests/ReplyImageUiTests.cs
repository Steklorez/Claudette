using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.Core.Tests.Support;
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>
/// Images in a rendered reply (DESIGN.md §18, "Images in Claude's replies"): Claudette's own view in place of
/// LiveMarkdown's loader, so a web image waits for a click and nothing is requested before it.
/// </summary>
public class ReplyImageUiTests
{
    /// <summary>A 1×1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private const string Url = "https://example.com/charts/sales.png";

    [AvaloniaFact]
    public async Task A_web_image_in_a_reply_waits_for_a_click_and_one_in_the_tabs_folder_shows_at_once()
    {
        var http = new FakeHttpHandler().OnBytes(Url, Png);
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), http: http);
        await h.OpenTabAsync();
        await File.WriteAllBytesAsync(Path.Combine(h.WorkFolder, "chart.png"), Png, TestContext.Current.CancellationToken);
        var window = UiText.Show(new ShellView { DataContext = h.Shell });

        h.Transport.EmitTurn($"Here's the chart:\n\n![Sales by week]({Url})\n\nAnd the one I made:\n\n![Local chart](chart.png)\n\nSee the [docs](https://example.com/docs).");
        await UiText.SettleUntilAsync(window, () => Images(window).Count == 2 && Images(window)[1].ViewModel?.IsShown == true, "the reply's images");
        var (web, local) = (Images(window)[0], Images(window)[1]);

        // The web image: a placeholder naming where it's from, and nothing asked of the server.
        Assert.Equal(ReplyImageState.Waiting, web.ViewModel?.State);
        var shown = UiText.Describe(web);
        Assert.Contains("Sales by week", shown, StringComparison.Ordinal);
        Assert.Contains("Image from example.com, which loads only when you ask.", shown, StringComparison.Ordinal);
        Assert.Empty(http.Requests);
        // LiveMarkdown's own loader, which would have fetched it by itself, is never given an address.
        Assert.All(window.GetVisualDescendants().OfType<Image>(), i => Assert.Null(AsyncImageLoader.GetSource(i)));
        // The file in the tab's folder shows at once; the link stays a link.
        Assert.IsType<Bitmap>(local.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "Picture").Source);
        Assert.Contains("docs", UiText.Describe(window), StringComparison.Ordinal);

        var load = web.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == $"Load image from {Url}");
        var at = load.TranslatePoint(new Point(load.Bounds.Width / 2, load.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        await UiText.SettleUntilAsync(window, () => web.ViewModel?.IsShown == true, "the web image");
        Assert.Equal(Url, Assert.Single(http.Requests).RequestUri!.ToString());
        Assert.IsType<Bitmap>(web.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "Picture").Source);
    }

    private static List<ReplyImageView> Images(Window window) => [.. window.GetVisualDescendants().OfType<ReplyImageView>()];
}
