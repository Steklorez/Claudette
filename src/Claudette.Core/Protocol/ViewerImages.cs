using System.Buffers.Binary;
using Claudette.Core.Composer;

namespace Claudette.Core.Protocol;

/// <summary>
/// The images Claudette shows itself, in a reply and in its image window (DESIGN.md §18, "Opening images"): PNG, JPEG,
/// GIF, WebP and BMP, which Skia decodes on every platform. Any other image (SVG, TIFF, PSD) goes to the app the OS uses
/// for it.
/// </summary>
public static class ViewerImages
{
    /// <summary>The largest image shown, as for an attachment; a larger file goes to the OS's app.</summary>
    public const long MaxBytes = Attachments.MaxImageBytes;

    /// <summary>What a failed check says.</summary>
    public const string NotAnImage = "It isn't a PNG, JPEG, GIF, WebP or BMP image.";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    /// <summary>
    /// Whether a file's name, or a web address's path, says it's one: by its extension, before anything is read. Its
    /// first bytes have the last word (<see cref="DetectMediaType"/>).
    /// </summary>
    public static bool HasExtension(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Its media type from its first bytes, whatever its name says: the four the Messages API takes
    /// (<see cref="MessageImage.DetectMediaType"/>), and <c>image/bmp</c>. Null for anything else.
    /// </summary>
    public static string? DetectMediaType(ReadOnlySpan<byte> data) => MessageImage.DetectMediaType(data) ?? (IsBmp(data) ? "image/bmp" : null);

    /// <summary>
    /// The image file at <paramref name="path"/>, when its name and its first bytes say it's one and it's no larger than
    /// <see cref="MaxBytes"/>; null when it isn't, isn't there or can't be read.
    /// </summary>
    public static MessageImage? ReadFile(string path)
    {
        if (!HasExtension(path))
        {
            return null;
        }
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxBytes)
            {
                return null;
            }
            var data = File.ReadAllBytes(path);
            return DetectMediaType(data) is { } mediaType ? new MessageImage(mediaType, data) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary><c>BM</c>, then a header whose size is one of the BMP versions', so a text file that starts "BM" isn't one.</summary>
    private static bool IsBmp(ReadOnlySpan<byte> data) =>
        data.Length >= 18 && data.StartsWith("BM"u8) && BinaryPrimitives.ReadUInt32LittleEndian(data[14..18]) is 12 or 40 or 52 or 56 or 64 or 108 or 124;
}
