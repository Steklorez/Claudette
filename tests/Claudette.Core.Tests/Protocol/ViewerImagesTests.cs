using Claudette.Core.Protocol;

namespace Claudette.Core.Tests.Protocol;

/// <summary>The images Claudette shows itself, and an image file to show (DESIGN.md §18, "Opening images").</summary>
public sealed class ViewerImagesTests : IDisposable
{
    /// <summary>A 1×1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly string _folder = Directory.CreateTempSubdirectory("claudette-viewerimages-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A 1×1, 24-bit BMP.</summary>
    internal static byte[] Bmp()
    {
        var data = new byte[58];
        "BM"u8.CopyTo(data);
        BitConverter.GetBytes(58).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(1).CopyTo(data, 18);
        BitConverter.GetBytes(1).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        return data;
    }

    [Theory]
    [InlineData("menu.png", true)]
    [InlineData("Photo.JPG", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("/shots/loop.gif", true)]
    [InlineData("C:\\Art\\hero.webp", true)]
    [InlineData("icon.bmp", true)]
    [InlineData("logo.svg", false)]
    [InlineData("scan.tiff", false)]
    [InlineData("hero.psd", false)]
    [InlineData("png", false)]
    [InlineData("notes.md", false)]
    public void The_name_says_whether_its_one(string path, bool expected) => Assert.Equal(expected, ViewerImages.HasExtension(path));

    [Fact]
    public void The_first_bytes_say_what_it_is_including_BMP_but_not_text_that_starts_BM()
    {
        Assert.Equal("image/png", ViewerImages.DetectMediaType(Png));
        Assert.Equal("image/bmp", ViewerImages.DetectMediaType(Bmp()));
        Assert.Null(ViewerImages.DetectMediaType("BMW owners' club: the minutes of the meeting"u8));
        Assert.Null(ViewerImages.DetectMediaType("BM"u8));
        // Claude can't be sent a BMP, so an attachment still can't be one.
        Assert.Null(MessageImage.DetectMediaType(Bmp()));
    }

    [Fact]
    public void A_file_is_read_when_its_name_and_its_bytes_say_its_an_image_no_larger_than_20_MB()
    {
        var png = Write("menu.png", Png);
        var big = new byte[ViewerImages.MaxBytes + 1];
        Png.CopyTo(big, 0);

        var image = ViewerImages.ReadFile(png);
        Assert.Equal("image/png", image?.MediaType);
        Assert.Equal(Png, image?.Data);
        Assert.Equal("image/bmp", ViewerImages.ReadFile(Write("icon.bmp", Bmp()))?.MediaType);
        Assert.Null(ViewerImages.ReadFile(Write("notes.png", "not a picture"u8.ToArray())));
        Assert.Null(ViewerImages.ReadFile(Write("menu.dat", Png)));
        Assert.Null(ViewerImages.ReadFile(Write("huge.png", big)));
        Assert.Null(ViewerImages.ReadFile(Path.Combine(_folder, "gone.png")));
        // A folder by an image's name.
        Assert.Null(ViewerImages.ReadFile(Directory.CreateDirectory(Path.Combine(_folder, "shots.png")).FullName));
    }

    private string Write(string name, byte[] data)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }
}
