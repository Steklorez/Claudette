using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Tests.Support;

namespace Claudette.App.Tests;

/// <summary>
/// Images in replies (DESIGN.md §18, "Images in Claude's replies"): a web image or a file elsewhere waits for a click, a
/// file in the tab's folder shows at once, a network path never loads, and nothing is requested before the click.
/// </summary>
public class ReplyImageTests
{
    /// <summary>A 1×1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private const string Url = "https://example.com/charts/sales.png";

    [Fact]
    public async Task A_web_image_asks_nothing_of_the_server_until_its_clicked_and_then_shows_again_without_asking()
    {
        var http = new FakeHttpHandler().OnBytes(Url, Png);
        await using var h = new TabTestHarness(http: http);
        var tab = await h.OpenTabAsync();

        var image = new ReplyImageViewModel(Url, "Sales by week");
        // A button bound before the image is in a tab asks again once it is.
        Assert.False(image.LoadCommand.CanExecute(null));
        var asked = false;
        image.LoadCommand.CanExecuteChanged += (_, _) => asked = true;
        image.Attach(tab.ReplyImages, i => tab.OpenImageCommand.Execute(i));
        Assert.True(asked);

        Assert.Equal(ReplyImageState.Waiting, image.State);
        Assert.Equal("Sales by week", image.Title);
        Assert.Equal("Image from example.com, which loads only when you ask.", image.Caption);
        Assert.Equal(("Load image", $"Load image from {Url}"), (image.LoadText, image.LoadName));
        Assert.True(image.LoadCommand.CanExecute(null));
        Assert.Empty(http.Requests);

        await image.LoadCommand.ExecuteAsync(null);

        Assert.Equal(ReplyImageState.Shown, image.State);
        Assert.Equal(Png, image.Image?.Data);
        Assert.Single(http.Requests);

        // The reply built again, as it scrolls back into view: shown at once, without asking again.
        var again = Attached(tab, Url, "Sales by week");
        Assert.Equal(ReplyImageState.Shown, again.State);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task A_web_image_let_load_once_loads_by_itself_after_it_was_forgotten()
    {
        var http = new FakeHttpHandler().OnBytes(Url, Png);
        await using var h = new TabTestHarness(http: http);
        var tab = await h.OpenTabAsync();
        await Attached(tab, Url, "").LoadCommand.ExecuteAsync(null);
        for (var i = 0; i < ReplyImages.Kept; i++)
        {
            var other = $"https://example.com/{i}.png";
            http.OnBytes(other, Png);
            await tab.ReplyImages.LoadAsync(tab.ReplyImages.Classify(other), TestContext.Current.CancellationToken);
        }
        Assert.Null(tab.ReplyImages.Loaded(Url));

        var again = Attached(tab, Url, "");

        await TabTestHarness.Eventually(() => again.State == ReplyImageState.Shown, "the image");
        Assert.Equal(2, http.Requests.Count(r => r.RequestUri!.ToString() == Url));
    }

    [Fact]
    public async Task A_file_in_the_tabs_folder_shows_at_once_and_one_elsewhere_waits()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "out"));
        await File.WriteAllBytesAsync(Path.Combine(h.WorkFolder, "out", "chart.png"), Png, TestContext.Current.CancellationToken);
        var elsewhere = Path.Combine(h.Root, "elsewhere.png");
        await File.WriteAllBytesAsync(elsewhere, Png, TestContext.Current.CancellationToken);

        var inFolder = Attached(tab, "out/chart.png", "Chart");
        var outside = Attached(tab, elsewhere, "");

        await TabTestHarness.Eventually(() => inFolder.State == ReplyImageState.Shown, "the image in the folder");
        Assert.Equal(ReplyImageState.Waiting, outside.State);
        Assert.Equal(("Show image", $"Image at {elsewhere}, outside the tab's folder."), (outside.LoadText, outside.Caption));
        await outside.LoadCommand.ExecuteAsync(null);
        Assert.Equal(ReplyImageState.Shown, outside.State);
    }

    [Fact]
    public async Task An_image_in_a_network_folder_never_loads_and_one_outside_a_tab_doesnt_either()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        var network = Attached(tab, @"\\server\share\chart.png", "");
        var nowhere = new ReplyImageViewModel(Url, "");
        nowhere.Attach(null, null);

        Assert.Equal((ReplyImageState.Refused, "Images in network folders don't load."), (network.State, network.Caption));
        Assert.False(network.LoadCommand.CanExecute(null));
        Assert.Equal((ReplyImageState.Refused, "Images in replies show in a tab's conversation."), (nowhere.State, nowhere.Caption));
        Assert.False(nowhere.CanLoad);
    }

    [Fact]
    public async Task A_web_image_that_fails_says_why_and_can_be_tried_again()
    {
        var http = new FakeHttpHandler();
        await using var h = new TabTestHarness(http: http);
        var tab = await h.OpenTabAsync();
        var image = Attached(tab, Url, "");

        await image.LoadCommand.ExecuteAsync(null);

        Assert.Equal(ReplyImageState.Failed, image.State);
        Assert.Equal("Couldn't load it: example.com answered 404 Not Found.", image.Caption);
        http.OnBytes(Url, Png);
        Assert.True(image.LoadCommand.CanExecute(null));
        await image.LoadCommand.ExecuteAsync(null);
        Assert.Equal(ReplyImageState.Shown, image.State);
    }

    [Fact]
    public async Task A_shown_image_opens_full_size_named_for_it()
    {
        var http = new FakeHttpHandler().OnBytes(Url, Png);
        await using var h = new TabTestHarness(http: http);
        var tab = await h.OpenTabAsync();
        await File.WriteAllBytesAsync(Path.Combine(h.WorkFolder, "chart.png"), Png, TestContext.Current.CancellationToken);
        var opened = new List<ImageViewerViewModel>();
        tab.ImageRequested += opened.Add;
        var web = Attached(tab, Url, "Sales by week");
        var local = Attached(tab, "chart.png", "");
        await web.LoadCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => local.State == ReplyImageState.Shown, "the image in the folder");

        web.OpenCommand.Execute(null);
        local.OpenCommand.Execute(null);

        Assert.Equal(("Sales by week", "example.com · PNG 70 bytes", "sales.png", false), (opened[0].Title, opened[0].Details, opened[0].Image.FileName, opened[0].CanOpen));
        Assert.Equal(("Image", "In a reply · PNG 70 bytes", "chart.png", true), (opened[1].Title, opened[1].Details, opened[1].Image.FileName, opened[1].CanOpen));
        Assert.Equal(Path.Combine(h.WorkFolder, "chart.png"), opened[1].Image.FilePath);
        opened.ForEach(o => o.Dispose());
    }

    private static ReplyImageViewModel Attached(TabViewModel tab, string address, string alt)
    {
        var image = new ReplyImageViewModel(address, alt);
        image.Attach(tab.ReplyImages, i => tab.OpenImageCommand.Execute(i));
        return image;
    }
}
