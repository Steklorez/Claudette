using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Deventry.Core.Composer;

namespace Deventry.App.Services;

/// <summary>
/// Attached images (DESIGN.md §5, "Attachments") and images from tools' results (DESIGN.md §18, "Images from Claude's
/// tools"): bitmaps as bytes to send, and bytes as thumbnails, previews and full size to show.
/// </summary>
public static class ImageFiles
{
    /// <summary>Thumbnails are decoded at twice their shown height, for high-DPI screens.</summary>
    private const int ThumbnailHeight = 144;

    /// <summary>The JPEG quality for a bitmap too big as PNG: close to the original, at about a tenth of the size.</summary>
    internal const int JpegQuality = 90;

    /// <summary>
    /// A bitmap from the clipboard or a drag, as bytes to attach: PNG, or JPEG when the PNG would be over
    /// <see cref="Attachments.MaxImageBytes"/>, as a photo-like screenshot of a 5K screen (a game, say) can be.
    /// </summary>
    public static byte[] Encode(Bitmap bitmap) => Encode(options =>
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, options);
        return stream.ToArray();
    });

    /// <summary>The choice <see cref="Encode(Bitmap)"/> makes, given a way to save the bitmap.</summary>
    internal static byte[] Encode(Func<BitmapEncoderOptions, byte[]> save)
    {
        var png = save(PngBitmapEncoderOptions.Default);
        return png.LongLength <= Attachments.MaxImageBytes ? png : save(new JpegBitmapEncoderOptions { Quality = JpegQuality });
    }

    /// <summary>The previews of the images under a tool's card are 120 px high, decoded at twice that.</summary>
    private const int PreviewHeight = 240;

    /// <summary>Image bytes to a small bitmap; null when they can't be decoded.</summary>
    public static readonly IValueConverter Thumbnail = new FuncValueConverter<byte[]?, Bitmap?>(data =>
        Decode(data, stream => Bitmap.DecodeToHeight(stream, ThumbnailHeight, BitmapInterpolationMode.MediumQuality)));

    /// <summary>
    /// Image bytes to a preview for a tool's card (DESIGN.md §18, "Images from Claude's tools"); an image smaller than
    /// that is decoded as it is.
    /// </summary>
    public static readonly IValueConverter Preview = new FuncValueConverter<byte[]?, Bitmap?>(data => Decode(data, stream => Fit(stream, int.MaxValue, PreviewHeight)));

    /// <summary>
    /// Image bytes to a bitmap for a reply (DESIGN.md §18, "Images in Claude's replies"), which shows at most 960 by 480:
    /// decoded at no more than twice that, and an image smaller than that as it is.
    /// </summary>
    public static readonly IValueConverter Reply = new FuncValueConverter<byte[]?, Bitmap?>(data => Decode(data, stream => Fit(stream, 1920, 960)));

    /// <summary>Image bytes to a bitmap at full size, for the image's own window; null when they can't be decoded.</summary>
    public static readonly IValueConverter Full = new FuncValueConverter<byte[]?, Bitmap?>(data => Decode(data, stream => new Bitmap(stream)));

    /// <summary>A group's icon shows 14 pixels across (16 in the rail), decoded at no more than three times that.</summary>
    private const int GroupIconSize = 48;

    private static readonly Dictionary<string, (DateTime Written, long Length, Bitmap? Bitmap)> GroupIcons = [];

    /// <summary>
    /// A project's icon file (DESIGN.md §4, "Grouped by folder") to a small bitmap; null when it can't be read or decoded.
    /// Each is decoded once while its file stays the same, as the sidebar, the rail and Settings all show it.
    /// </summary>
    public static readonly IValueConverter GroupIcon = new FuncValueConverter<string?, Bitmap?>(path => path is null ? null : LoadGroupIcon(path));

    private static Bitmap? LoadGroupIcon(string path)
    {
        try
        {
            var info = new FileInfo(path);
            lock (GroupIcons)
            {
                if (GroupIcons.TryGetValue(path, out var known) && known.Written == info.LastWriteTimeUtc && known.Length == info.Length)
                {
                    return known.Bitmap;
                }
            }
            var bitmap = Decode(File.ReadAllBytes(path), stream => Fit(stream, GroupIconSize, GroupIconSize));
            lock (GroupIcons)
            {
                GroupIcons[path] = (info.LastWriteTimeUtc, info.Length, bitmap);
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The image, scaled down to fit <paramref name="maxWidth"/> by <paramref name="maxHeight"/> pixels when it's larger.</summary>
    private static Bitmap Fit(Stream stream, int maxWidth, int maxHeight)
    {
        var bitmap = new Bitmap(stream);
        var (width, height) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var scale = Math.Min(1, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        if (scale >= 1)
        {
            return bitmap;
        }
        using (bitmap)
        {
            return bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale))));
        }
    }

    private static Bitmap? Decode(byte[]? data, Func<Stream, Bitmap> decode)
    {
        if (data is not { Length: > 0 })
        {
            return null;
        }
        try
        {
            using var stream = new MemoryStream(data);
            return decode(stream);
        }
        catch (Exception)
        {
            // A format Skia can't decode (for example an animated WebP it doesn't support); show nothing.
            return null;
        }
    }
}
