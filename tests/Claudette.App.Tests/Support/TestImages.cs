namespace Claudette.App.Tests.Support;

/// <summary>Small images, made byte by byte, for the image window's tests (DESIGN.md §18, "Opening images").</summary>
internal static class TestImages
{
    /// <summary>A 1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>A 1×1, 24-bit BMP.</summary>
    public static byte[] Bmp()
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
        BitConverter.GetBytes(4).CopyTo(data, 34);
        data[54] = 0xFF;
        return data;
    }

    /// <summary>
    /// A 1×1 GIF with a frame for each delay (in hundredths of a second), red and blue in turn. It loops
    /// <paramref name="loops"/> times more after the first (0 for ever) when given; without, it plays once.
    /// </summary>
    public static byte[] Gif(int[] delays, int? loops = 0)
    {
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        // 1×1, a global table of two colors: red and blue.
        gif.AddRange([1, 0, 1, 0, 0x80, 0, 0, 0xFF, 0, 0, 0, 0, 0xFF]);
        if (loops is { } count)
        {
            gif.AddRange([0x21, 0xFF, 0x0B]);
            gif.AddRange("NETSCAPE2.0"u8.ToArray());
            gif.AddRange([3, 1, (byte)(count & 0xFF), (byte)(count >> 8), 0]);
        }
        for (var i = 0; i < delays.Length; i++)
        {
            // Its delay, then the frame: one pixel, coded as clear, the color's index, end.
            gif.AddRange([0x21, 0xF9, 4, 0, (byte)(delays[i] & 0xFF), (byte)(delays[i] >> 8), 0, 0]);
            gif.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
            gif.AddRange([2, 2, (byte)(i % 2 == 0 ? 0x44 : 0x4C), 0x01, 0]);
        }
        gif.Add(0x3B);
        return [.. gif];
    }
}
