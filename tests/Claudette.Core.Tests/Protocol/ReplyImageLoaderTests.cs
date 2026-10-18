using System.Net;
using Claudette.Core.Protocol;
using Claudette.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Protocol;

/// <summary>Loading an image in a reply once it may (DESIGN.md §18, "Images in Claude's replies"). Nothing reaches the network.</summary>
public sealed class ReplyImageLoaderTests : IDisposable
{
    /// <summary>A 1×1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private const string Url = "https://example.com/chart.png";

    private readonly string _folder = Directory.CreateTempSubdirectory("claudette-replyimages-").FullName;

    private readonly FakeTimeProvider _time = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_web_image_loads_through_the_client_it_is_given()
    {
        var http = new FakeHttpHandler().OnBytes(Url, Png);

        var image = await Loader(http).LoadAsync(Web(), TestContext.Current.CancellationToken);

        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(Png, image.Data);
        Assert.Equal(Url, Assert.Single(http.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task A_web_page_that_isnt_an_image_or_a_server_error_says_so()
    {
        var http = new FakeHttpHandler()
            .On(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Not an image</html>") });

        Assert.Equal("It isn't a PNG, JPEG, GIF, WebP or BMP image.", await FailureAsync(Loader(http), Web()));
        Assert.Equal("example.com answered 404 Not Found.", await FailureAsync(Loader(new FakeHttpHandler()), Web()));
        Assert.Equal("example.com couldn't be reached: offline", await FailureAsync(Loader(new ThrowingHandler()), Web()));
    }

    [Fact]
    public async Task An_image_over_20_MB_isnt_loaded_whether_or_not_the_server_says_its_size()
    {
        var big = new byte[(int)ReplyImageLoader.MaxBytes + 1];
        Png.CopyTo(big, 0);
        var said = new FakeHttpHandler().OnBytes(Url, big);
        var unsaid = new FakeHttpHandler().On(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnsizedContent(big) });

        Assert.Equal("It's over 20 MB.", await FailureAsync(Loader(said), Web()));
        Assert.Equal("It's over 20 MB.", await FailureAsync(Loader(unsaid), Web()));
    }

    [Fact]
    public async Task A_web_image_that_doesnt_arrive_gives_up_on_the_clock()
    {
        // The timeout starts before the request goes out, so the clock can move on at once.
        var loading = FailureAsync(Loader(new HangingHandler()), Web());
        Assert.False(loading.IsCompleted);

        _time.Advance(ReplyImageLoader.WebTimeout);

        Assert.Equal("example.com took too long to answer.", await loading);
    }

    [Fact]
    public async Task A_file_loads_from_the_disk_and_one_that_isnt_an_image_or_isnt_there_says_so()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, "chart.png"), Png, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.png"), "not really a picture", TestContext.Current.CancellationToken);
        var loader = Loader(new FakeHttpHandler());

        var image = await loader.LoadAsync(ReplyImageAddress.Classify("chart.png", _folder), TestContext.Current.CancellationToken);

        Assert.Equal(Png, image.Data);
        Assert.Equal("It isn't a PNG, JPEG, GIF, WebP or BMP image.", await FailureAsync(loader, ReplyImageAddress.Classify("notes.png", _folder)));
        Assert.Equal("There's no such file.", await FailureAsync(loader, ReplyImageAddress.Classify("gone.png", _folder)));
    }

    [Fact]
    public async Task A_BMP_loads_too_since_Claudette_shows_one_as_it_does_the_others()
    {
        var bmp = ViewerImagesTests.Bmp();
        await File.WriteAllBytesAsync(Path.Combine(_folder, "icon.bmp"), bmp, TestContext.Current.CancellationToken);

        var image = await Loader(new FakeHttpHandler()).LoadAsync(ReplyImageAddress.Classify("icon.bmp", _folder), TestContext.Current.CancellationToken);

        Assert.Equal("image/bmp", image.MediaType);
        Assert.Equal(bmp, image.Data);
    }

    [Fact]
    public async Task A_data_image_needs_no_loading_and_a_refused_one_says_why()
    {
        var http = new FakeHttpHandler();
        var loader = Loader(http);

        var image = await loader.LoadAsync(ReplyImageAddress.Classify($"data:image/png;base64,{Convert.ToBase64String(Png)}", _folder), TestContext.Current.CancellationToken);

        Assert.Equal(Png, image.Data);
        Assert.Equal("Images in network folders don't load.", await FailureAsync(loader, ReplyImageAddress.Classify(@"\\server\share\a.png", _folder)));
        Assert.Empty(http.Requests);
    }

    private ReplyImageLoader Loader(HttpMessageHandler handler) => new(new HttpClient(handler), _time);

    private ReplyImageAddress Web() => ReplyImageAddress.Classify(Url, _folder);

    private static async Task<string> FailureAsync(ReplyImageLoader loader, ReplyImageAddress address) =>
        (await Assert.ThrowsAsync<ReplyImageException>(() => loader.LoadAsync(address, TestContext.Current.CancellationToken))).Message;

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A body that doesn't say how long it is.</summary>
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
