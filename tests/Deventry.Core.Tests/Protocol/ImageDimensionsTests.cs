using Deventry.Core.Protocol;
using Deventry.Core.Tests.Sessions;

namespace Deventry.Core.Tests.Protocol;

/// <summary>An image's size from its header, for what it counts as in tokens (DESIGN.md §18, "Token breakdown").</summary>
public class ImageDimensionsTests
{
    [Fact]
    public void A_png_has_its_size_in_its_header() =>
        Assert.Equal(new ImageSize(1280, 720), ImageDimensions.Read(TurnCallsTests.Png(1280, 720)));

    [Fact]
    public void A_gif_has_its_size_after_its_signature() =>
        Assert.Equal(new ImageSize(320, 200), ImageDimensions.Read([.. "GIF89a"u8, 0x40, 0x01, 0xC8, 0x00]));

    [Fact]
    public void A_jpeg_has_its_size_in_its_first_frame_after_the_segments_before_it()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,
            // APP0, 16 bytes with its length.
            0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
            // A Huffman table, whose marker is in the start-of-frame range but isn't one.
            0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00,
            // SOF2: precision, then the height (600) and width (800).
            0xFF, 0xC2, 0x00, 0x11, 0x08, 0x02, 0x58, 0x03, 0x20, 0x03, 0x01, 0x22, 0x00,
        ];
        Assert.Equal(new ImageSize(800, 600), ImageDimensions.Read(jpeg));
    }

    [Fact]
    public void A_webp_has_its_size_in_its_first_chunk_lossy_lossless_or_extended()
    {
        Assert.Equal(new ImageSize(640, 480), ImageDimensions.Read(WebP("VP8 ", [0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x80, 0x02, 0xE0, 0x01])));
        // Lossless packs width − 1 and height − 1 in 14 bits each: 399 and 299.
        Assert.Equal(new ImageSize(400, 300), ImageDimensions.Read(WebP("VP8L", [0x2F, 0x8F, 0xC1, 0x4A, 0x00, 0, 0, 0, 0, 0])));
        // Extended has width − 1 and height − 1 in three bytes each: 1919 and 1079.
        Assert.Equal(new ImageSize(1920, 1080), ImageDimensions.Read(WebP("VP8X", [0, 0, 0, 0, 0x7F, 0x07, 0x00, 0x37, 0x04, 0x00])));
    }

    [Fact]
    public void Anything_else_or_a_header_cut_short_has_no_size()
    {
        Assert.Null(ImageDimensions.Read("not an image at all, just text"u8));
        Assert.Null(ImageDimensions.Read(TurnCallsTests.Png(1280, 720)[..20]));
        Assert.Null(ImageDimensions.Read([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Null(ImageDimensions.Read(TurnCallsTests.Png(0, 720)));
    }

    /// <summary>A WebP's RIFF header and its first chunk, <paramref name="chunk"/>, with <paramref name="data"/> from where the chunk's data starts.</summary>
    private static byte[] WebP(string chunk, byte[] data) =>
        [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. System.Text.Encoding.ASCII.GetBytes(chunk), 0, 0, 0, 0, .. data];
}
