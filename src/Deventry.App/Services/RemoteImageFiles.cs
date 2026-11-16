using Deventry.App.Conversation;
using Deventry.Core.Protocol;
using Deventry.Core.Remote;
using SkiaSharp;

namespace Deventry.App.Services;

/// <summary>
/// The images a turn made, ready for the remote clients that send them (DESIGN.md §18, "Remote clients", "Images"): of
/// the agent's images the turn showed, the ones it made rather than only read, each as a PNG or a JPEG that both
/// services take. Off the UI thread: it reads files.
/// </summary>
public static class RemoteImageFiles
{
    /// <summary>
    /// Of <paramref name="seen"/>, the agent's images the turn showed, oldest first, those it made: an image with no file
    /// (a screenshot, a tool's picture), or a file written since <paramref name="since"/> (a chart a script drew, a PNG
    /// Claude wrote). An image it only read, which was there before, isn't, unless <paramref name="withRead"/> says to
    /// take those too (<b>Send the images Claude reads</b>), marked <see cref="RemoteImage.WasRead"/>. The newest
    /// <see cref="RemoteImage.MostPerTurn"/> that can be sent, oldest first, and how many more there were.
    /// </summary>
    public static (IReadOnlyList<RemoteImage> Images, int LeftOut) Made(IReadOnlyList<MediaEntry> seen, DateTimeOffset since, bool withRead = false)
    {
        var taken = seen.Select(entry => (Entry: entry, WasRead: !IsMade(entry, since))).Where(e => !e.WasRead || (withRead && HasFile(e.Entry))).ToList();
        var images = new List<RemoteImage>();
        var tried = 0;
        // The newest first, until an album is full.
        for (var i = taken.Count - 1; i >= 0 && images.Count < RemoteImage.MostPerTurn; i--)
        {
            tried++;
            if (Read(taken[i].Entry) is { } image)
            {
                images.Add(image with { WasRead = taken[i].WasRead });
            }
        }
        images.Reverse();
        return (images, taken.Count - tried);
    }

    /// <summary>An image the turn showed from a file: one Claude may only have read.</summary>
    private static bool HasFile(MediaEntry entry) => (entry.Path ?? entry.Image?.FilePath) is { Length: > 0 };

    private static bool IsMade(MediaEntry entry, DateTimeOffset since)
    {
        if (entry.Image is { FilePath: null })
        {
            return true;
        }
        if ((entry.Path ?? entry.Image?.FilePath) is not { } path)
        {
            return false;
        }
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.LastWriteTimeUtc >= since.UtcDateTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static RemoteImage? Read(MediaEntry entry)
    {
        if (entry.Image is { FilePath: null } shown)
        {
            return Prepare(entry.Name, shown.Image.Data);
        }
        return (entry.Path ?? entry.Image?.FilePath) is { } path && ViewerImages.ReadFile(path) is { } file ? Prepare(Path.GetFileName(path), file.Data) : null;
    }

    /// <summary>
    /// <paramref name="data"/> as an image both services take: a PNG or a JPEG as it is, when it's no larger than
    /// <see cref="RemoteImage.MaxBytes"/> and <see cref="RemoteImage.MaxSide"/> across; anything else (a GIF's first
    /// frame, a WebP, a BMP, one too large) drawn again as a PNG, scaled down to fit, or a JPEG when the PNG is too large
    /// still. Null for what isn't an image the image window shows, or can't be read.
    /// </summary>
    public static RemoteImage? Prepare(string name, byte[] data)
    {
        if (ViewerImages.DetectMediaType(data) is not { } mediaType)
        {
            return null;
        }
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (mediaType is "image/png" or "image/jpeg" && data.Length <= RemoteImage.MaxBytes && Math.Max(width, height) <= RemoteImage.MaxSide)
        {
            return new RemoteImage(Named(name, mediaType), mediaType, data, width, height);
        }
        using var image = MediaThumbnails.Decode(codec, RemoteImage.MaxSide, RemoteImage.MaxSide);
        if (image is null)
        {
            return null;
        }
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        if (png is not null && png.Size <= RemoteImage.MaxBytes)
        {
            return new RemoteImage(Named(name, "image/png"), "image/png", png.ToArray(), image.Width, image.Height);
        }
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return jpeg is not null && jpeg.Size <= RemoteImage.MaxBytes
            ? new RemoteImage(Named(name, "image/jpeg"), "image/jpeg", jpeg.ToArray(), image.Width, image.Height)
            : null;
    }

    /// <summary>A file's name for the image, with its kind's extension: <c>chart.png</c>, <c>Screenshot.png</c>.</summary>
    private static string Named(string name, string mediaType)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]).Trim();
        var stem = Path.GetFileNameWithoutExtension(safe) is { Length: > 0 } s ? s : "image";
        return stem + (mediaType == "image/jpeg" ? ".jpg" : ".png");
    }
}
