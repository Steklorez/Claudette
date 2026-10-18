using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Claudette.App.Services;

/// <summary>
/// An animated GIF or WebP's frames, for the image window (DESIGN.md §18, "Opening images"), read with Skia's codec:
/// Avalonia's <see cref="Bitmap"/> decodes only the first. Frames are decoded one at a time into one buffer, each over the
/// one before it where it can be, so a long animation holds one frame, not all of them. Safe to use from any thread.
/// </summary>
public sealed class ImageAnimation : IDisposable
{
    /// <summary>A frame this short or shorter shows for <see cref="ShortFrameShownFor"/>, as browsers do.</summary>
    internal static readonly TimeSpan ShortestFrame = TimeSpan.FromMilliseconds(10);

    internal static readonly TimeSpan ShortFrameShownFor = TimeSpan.FromMilliseconds(100);

    /// <summary>A larger canvas isn't animated, since its one buffer would be over 256 MB.</summary>
    private const long MaxPixels = 64L * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly SKData _data;
    private readonly SKCodec _codec;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly SKBitmap _buffer;

    /// <summary>The frame in <see cref="_buffer"/>, or -1.</summary>
    private int _decoded = -1;

    private bool _disposed;

    private ImageAnimation(SKData data, SKCodec codec, SKCodecFrameInfo[] frames)
    {
        _data = data;
        _codec = codec;
        _frames = frames;
        _buffer = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Size = new PixelSize(codec.Info.Width, codec.Info.Height);
    }

    /// <summary>The animation in a GIF or WebP; null for a still image, any other type, or one Skia can't read.</summary>
    public static ImageAnimation? From(string mediaType, byte[] data)
    {
        if (mediaType is not ("image/gif" or "image/webp") || data.Length == 0)
        {
            return null;
        }
        var skData = SKData.CreateCopy(data);
        SKCodec? codec = null;
        try
        {
            codec = SKCodec.Create(skData);
            if (codec is not null && codec.FrameCount > 1 && (long)codec.Info.Width * codec.Info.Height is > 0 and <= MaxPixels)
            {
                return new ImageAnimation(skData, codec, codec.FrameInfo);
            }
        }
        catch (Exception)
        {
            // Not one Skia reads; it shows still, as Avalonia decodes it.
        }
        codec?.Dispose();
        skData.Dispose();
        return null;
    }

    public PixelSize Size { get; }

    public int FrameCount => _frames.Length;

    /// <summary>How many times it plays again after the first time: -1 for ever, 0 for once.</summary>
    public int Repetitions => _codec.RepetitionCount;

    /// <summary>How long <paramref name="frame"/> shows: as long as it says, unless that's too short to be meant.</summary>
    public TimeSpan Duration(int frame)
    {
        var duration = TimeSpan.FromMilliseconds(_frames[frame].Duration);
        return duration <= ShortestFrame ? ShortFrameShownFor : duration;
    }

    /// <summary>The frame as a bitmap to show, which the caller disposes; null when it can't be decoded.</summary>
    public Bitmap? Render(int frame)
    {
        lock (_lock)
        {
            return Decode(frame)
                ? new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, _buffer.GetPixels(), Size, new Vector(96, 96), _buffer.RowBytes)
                : null;
        }
    }

    /// <summary>The frame as a PNG, for <b>Copy image</b>; null when it can't be decoded.</summary>
    public byte[]? EncodePng(int frame)
    {
        lock (_lock)
        {
            if (!Decode(frame))
            {
                return null;
            }
            using var image = SKImage.FromBitmap(_buffer);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
    }

    private bool Decode(int frame)
    {
        if (_disposed || frame < 0 || frame >= _frames.Length)
        {
            return false;
        }
        if (frame == _decoded)
        {
            return true;
        }
        var required = _frames[frame].RequiredFrame;
        // Over the frame in the buffer when the codec can draw on it; else from nothing, the codec decoding first the
        // frames it needs.
        var prior = required >= 0 && CanDrawOver(required, frame) ? _decoded : -1;
        if (prior < 0)
        {
            _buffer.Erase(SKColors.Transparent);
        }
        var result = _codec.GetPixels(_buffer.Info, _buffer.GetPixels(), new SKCodecOptions(frame, prior));
        _decoded = result is SKCodecResult.Success or SKCodecResult.IncompleteInput ? frame : -1;
        return _decoded == frame;
    }

    /// <summary>
    /// The buffer holds a frame <paramref name="frame"/> can be drawn over: no earlier than the one it needs, before it,
    /// and not one that's undone before the next is drawn.
    /// </summary>
    private bool CanDrawOver(int required, int frame) =>
        _decoded >= required && _decoded < frame && _frames[_decoded].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious;

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _buffer.Dispose();
            _codec.Dispose();
            _data.Dispose();
        }
    }
}
