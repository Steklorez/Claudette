using System.Buffers.Binary;

namespace Deventry.Core.Protocol;

/// <summary>
/// An image's width and height, from its header, for what it counts as in tokens (DESIGN.md §18, "Token breakdown"): PNG,
/// JPEG, GIF and WebP, the types the Messages API accepts.
/// </summary>
public static class ImageDimensions
{
    /// <summary>The image's size, or null when its header isn't one of the four or can't be read.</summary>
    public static ImageSize? Read(ReadOnlySpan<byte> data) => MessageImage.DetectMediaType(data) switch
    {
        "image/png" when data.Length >= 24 => Size(BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..])),
        "image/gif" when data.Length >= 10 => Size(BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..])),
        "image/jpeg" => Jpeg(data),
        "image/webp" => WebP(data),
        _ => null,
    };

    /// <summary>A JPEG's size, from its first start-of-frame marker.</summary>
    private static ImageSize? Jpeg(ReadOnlySpan<byte> data)
    {
        var at = 2;
        while (at + 9 < data.Length)
        {
            if (data[at] != 0xFF)
            {
                return null;
            }
            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            // Start of frame: SOF0 to SOF15, but DHT (C4), JPG (C8) and DAC (CC) share the range.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return Size(BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]));
            }
            at += 2 + length;
        }
        return null;
    }

    /// <summary>A WebP's size, from its first chunk: lossy (VP8), lossless (VP8L) or extended (VP8X).</summary>
    private static ImageSize? WebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
        {
            return null;
        }
        return (data[12], data[13], data[14], data[15]) switch
        {
            ((byte)'V', (byte)'P', (byte)'8', (byte)' ') => Size(BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF),
            ((byte)'V', (byte)'P', (byte)'8', (byte)'L') => Size(1 + (data[21] | ((data[22] & 0x3F) << 8)), 1 + ((data[22] >> 6) | (data[23] << 2) | ((data[24] & 0x0F) << 10))),
            ((byte)'V', (byte)'P', (byte)'8', (byte)'X') => Size(1 + (data[24] | (data[25] << 8) | (data[26] << 16)), 1 + (data[27] | (data[28] << 8) | (data[29] << 16))),
            _ => null,
        };
    }

    private static ImageSize? Size(int width, int height) => width > 0 && height > 0 ? new ImageSize(width, height) : null;
}
