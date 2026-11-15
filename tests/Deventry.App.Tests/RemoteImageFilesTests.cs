using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.Core.Remote;
using SkiaSharp;

namespace Deventry.App.Tests;

/// <summary>A turn's images made ready for the remote clients (DESIGN.md §18, "Remote clients", "Images").</summary>
public class RemoteImageFilesTests
{
    [Fact]
    public void A_png_within_the_limits_goes_as_it_is_named_for_its_kind()
    {
        var image = RemoteImageFiles.Prepare("Image", TestImages.Png)!;

        Assert.Equal(("Image.png", "image/png", 1, 1), (image.Name, image.MediaType, image.Width, image.Height));
        Assert.Same(TestImages.Png, image.Data);
    }

    [Fact]
    public void A_gif_or_a_bmp_is_drawn_again_as_a_png()
    {
        foreach (var (name, data) in new[] { ("loop.gif", TestImages.Gif([10, 10])), ("old.bmp", TestImages.Bmp()) })
        {
            var image = RemoteImageFiles.Prepare(name, data)!;

            Assert.Equal("image/png", image.MediaType);
            Assert.Equal(Path.ChangeExtension(name, ".png"), image.Name);
            Assert.Equal("image/png", Core.Protocol.ViewerImages.DetectMediaType(image.Data));
        }
    }

    [Fact]
    public void One_larger_than_a_phone_shows_is_scaled_down_keeping_its_shape()
    {
        using var bitmap = new SKBitmap(RemoteImage.MaxSide * 2, 1000);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);

        var image = RemoteImageFiles.Prepare("wide.png", encoded.ToArray())!;

        Assert.Equal((RemoteImage.MaxSide, 500), (image.Width, image.Height));
        Assert.True(image.Data.Length <= RemoteImage.MaxBytes);
    }

    [Fact]
    public void What_isnt_an_image_the_window_shows_isnt_sent() =>
        Assert.Null(RemoteImageFiles.Prepare("logo.svg", "<svg/>"u8.ToArray()));
}
