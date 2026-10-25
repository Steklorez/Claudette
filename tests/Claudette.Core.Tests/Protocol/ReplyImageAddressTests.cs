using Claudette.Core.Protocol;

namespace Claudette.Core.Tests.Protocol;

/// <summary>Which images in a reply load at once, which wait for a click, and which never load (DESIGN.md §18, "Images in Claude's replies").</summary>
public sealed class ReplyImageAddressTests : IDisposable
{
    /// <summary>A 1×1 PNG.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private readonly string _root = Directory.CreateTempSubdirectory("claudette-replyimages-").FullName;

    private string Folder => Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_data_image_shows_at_once_as_what_its_bytes_say_it_is()
    {
        var address = ReplyImageAddress.Classify($"data:image/jpeg;base64,{Png}", Folder);

        Assert.Equal(ReplyImageKind.Inline, address.Kind);
        Assert.True(address.LoadsAtOnce);
        Assert.Equal("image/png", address.Image?.MediaType);
        Assert.Equal(Convert.FromBase64String(Png), address.Image?.Data);
    }

    [Theory]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:image/png,not-base64")]
    [InlineData("data:")]
    public void A_data_address_that_isnt_an_image_the_app_shows_never_loads(string address)
    {
        var classified = ReplyImageAddress.Classify(address, Folder);

        Assert.Equal(ReplyImageKind.Refused, classified.Kind);
        Assert.Equal("It isn't a PNG, JPEG, GIF, WebP or BMP image.", classified.Reason);
    }

    [Theory]
    [InlineData("https://example.com/chart.png", "example.com")]
    [InlineData("http://images.example.org/a/b.gif?size=2", "images.example.org")]
    public void A_web_image_waits_for_a_click(string address, string host)
    {
        var classified = ReplyImageAddress.Classify(address, Folder);

        Assert.Equal(ReplyImageKind.Web, classified.Kind);
        Assert.False(classified.LoadsAtOnce);
        Assert.Equal(new Uri(address), classified.Url);
        Assert.Equal(host, classified.Origin);
    }

    [Theory]
    [InlineData("chart.png", "chart.png")]
    [InlineData("./out/chart.png", "out/chart.png")]
    [InlineData("out/my%20chart.png", "out/my chart.png")]
    public void A_file_in_the_tabs_folder_shows_at_once(string address, string relative)
    {
        Directory.CreateDirectory(Path.Combine(Folder, "out"));

        var classified = ReplyImageAddress.Classify(address, Folder);

        Assert.Equal(ReplyImageKind.InFolder, classified.Kind);
        Assert.True(classified.LoadsAtOnce);
        Assert.Equal(Path.GetFullPath(Path.Combine(Folder, relative)), classified.Path);
        // By its full path too.
        Assert.Equal(ReplyImageKind.InFolder, ReplyImageAddress.Classify(classified.Path!, Folder).Kind);
        Assert.Equal(ReplyImageKind.InFolder, ReplyImageAddress.Classify(new Uri(classified.Path!).AbsoluteUri, Folder).Kind);
    }

    [Fact]
    public void A_markdown_files_image_is_read_against_its_own_folder_and_loads_at_once_only_inside_the_project()
    {
        // DESIGN.md §18, "Markdown preview": docs/index.md's screenshots/menu.png is docs/screenshots/menu.png.
        var docs = Directory.CreateDirectory(Path.Combine(Folder, "docs")).FullName;

        var inside = ReplyImageAddress.Classify("screenshots/menu.png", Folder, docs);
        Assert.Equal((ReplyImageKind.InFolder, Path.Combine(docs, "screenshots", "menu.png")), (inside.Kind, inside.Path));
        var up = ReplyImageAddress.Classify("../logo.png", Folder, docs);
        Assert.Equal((ReplyImageKind.InFolder, Path.Combine(Folder, "logo.png")), (up.Kind, up.Path));
        // Out of the project, it waits for a click as from a reply.
        var outside = ReplyImageAddress.Classify("../../elsewhere.png", Folder, docs);
        Assert.Equal((ReplyImageKind.LocalFile, Path.Combine(_root, "elsewhere.png")), (outside.Kind, outside.Path));
    }

    [Fact]
    public void A_file_outside_the_tabs_folder_waits_for_a_click()
    {
        var outside = Path.Combine(_root, "elsewhere.png");

        Assert.Equal((ReplyImageKind.LocalFile, outside), Kind("../elsewhere.png"));
        Assert.Equal((ReplyImageKind.LocalFile, outside), Kind(outside));
        Assert.Equal((ReplyImageKind.LocalFile, outside), Kind(new Uri(outside).AbsoluteUri));
        // A folder whose name starts the same isn't inside it.
        Assert.Equal(ReplyImageKind.LocalFile, Kind(Folder + "-other/x.png").Kind);
        Assert.Equal(outside, ReplyImageAddress.Classify(outside, Folder).Origin);

        (ReplyImageKind Kind, string? Path) Kind(string address) => ReplyImageAddress.Classify(address, Folder) is var c ? (c.Kind, c.Path) : default;
    }

    [Fact]
    public void A_link_in_the_tabs_folder_could_lead_anywhere_so_what_is_behind_it_waits_for_a_click()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Folder, "linked"), outside);
            File.CreateSymbolicLink(Path.Combine(Folder, "linked.png"), Path.Combine(outside, "a.png"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This machine can't make symbolic links: {ex.Message}");
        }

        Assert.Equal(ReplyImageKind.LocalFile, ReplyImageAddress.Classify("linked/a.png", Folder).Kind);
        Assert.Equal(ReplyImageKind.LocalFile, ReplyImageAddress.Classify("linked.png", Folder).Kind);

        // One that says it goes to a network folder never loads; where it goes is read from the link, not visited.
        File.CreateSymbolicLink(Path.Combine(Folder, "share.png"), "//server/share/a.png");
        var share = ReplyImageAddress.Classify("share.png", Folder);
        Assert.Equal((ReplyImageKind.Refused, "Images in network folders don't load."), (share.Kind, share.Reason));
    }

    [Theory]
    [InlineData(@"\\server\share\chart.png")]
    [InlineData("//server/share/chart.png")]
    [InlineData("file://server/share/chart.png")]
    public void An_image_in_a_network_folder_never_loads(string address)
    {
        var classified = ReplyImageAddress.Classify(address, Folder);

        Assert.Equal(ReplyImageKind.Refused, classified.Kind);
        Assert.Equal("Images in network folders don't load.", classified.Reason);
    }

    [Theory]
    [InlineData("ftp://example.com/chart.png", "\"ftp:\" images don't load.")]
    [InlineData("javascript:alert(1)", "\"javascript:\" images don't load.")]
    [InlineData("  ", "It has no address.")]
    public void Anything_else_never_loads(string address, string reason)
    {
        var classified = ReplyImageAddress.Classify(address, Folder);

        Assert.Equal((ReplyImageKind.Refused, reason), (classified.Kind, classified.Reason));
        Assert.Equal(address, classified.Origin);
    }
}
