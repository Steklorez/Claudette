using Claudette.App.Services;
using Claudette.Core.Files;
using Claudette.Core.ProjectTools;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// An image full size in a window of its own (DESIGN.md §18, "Images from Claude's tools", "Opening images"): from a
/// tool's result, a reply, or an image file or web image clicked. It has <b>Copy image</b>, <b>Save as…</b> and, for a
/// file, <b>Open</b> (<b>Open in browser</b> for a web image). An animated GIF or WebP plays, with <b>Pause</b> and
/// <b>Play</b>; with motion reduced it opens on its first frame.
/// </summary>
public sealed partial class ImageViewerViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly UiTimeout _copied;
    private readonly UiTimeout _nextFrame;

    /// <summary>How many times it has played through since <b>Play</b>.</summary>
    private int _plays;

    public ImageViewerViewModel(ToolImage image, AppServices services)
    {
        Image = image;
        _services = services;
        _copied = new UiTimeout(services.Time, services.Dispatcher);
        _nextFrame = new UiTimeout(services.Time, services.Dispatcher);
        Animation = ImageAnimation.From(image.Image.MediaType, image.Image.Data);
        Details = string.Join(" · ", new[]
        {
            image.Tool,
            $"{TypeName} {FolderCleaner.FormatSize(image.Image.Data.LongLength)}",
            Animation is { } animation ? $"{animation.FrameCount} frames" : null,
            image.ScaledFrom is { } original ? $"scaled down from {original} to send" : null,
        }.OfType<string>());
        if (Animation is not null && !services.ReduceMotion)
        {
            Play();
        }
    }

    public ToolImage Image { get; }

    /// <summary>The window's title: the file's name, or "report.pdf, page 3".</summary>
    public string Title => Image.Name;

    /// <summary>The image's bytes, which the view decodes.</summary>
    public byte[] Data => Image.Image.Data;

    /// <summary>"Read · PNG 12 KB · scaled down from 3000 × 1000 to send", "docs/intro.gif · GIF 2.4 MB · 48 frames".</summary>
    public string Details { get; }

    /// <summary>The file it's from, which can be opened in the app the OS uses for it, or the web address, in the browser.</summary>
    public bool CanOpen => Image.FilePath is not null || IsWebImage;

    private bool IsWebImage => Image.Url?.Scheme is "http" or "https";

    /// <summary><b>Open</b> for a file, <b>Open in browser</b> for a web image.</summary>
    public string OpenText => Image.FilePath is null && IsWebImage ? "Open in browser" : "Open";

    public string OpenTip => Image.FilePath is null && IsWebImage
        ? "Open the image's address in your browser"
        : "Open the file in the app your system uses for it";

    /// <summary>Its frames, for an animated GIF or WebP; null for a still image.</summary>
    public ImageAnimation? Animation { get; }

    public bool IsAnimated => Animation is not null;

    /// <summary>The frame showing, which the view decodes as it changes.</summary>
    [ObservableProperty]
    public partial int Frame { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    public partial bool IsPlaying { get; private set; }

    /// <summary><b>Pause</b> while it plays, else <b>Play</b>.</summary>
    public string PlayText => IsPlaying ? "Pause" : "Play";

    /// <summary><b>Copy image</b> says "Copied" for a moment.</summary>
    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    /// <summary>Why the last save didn't work; null when it did, or before one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    private string TypeName => Image.Image.MediaType switch
    {
        "image/jpeg" => "JPEG",
        "image/gif" => "GIF",
        "image/webp" => "WebP",
        "image/bmp" => "BMP",
        _ => "PNG",
    };

    /// <summary><b>Copy image</b>: the frame showing, for an animated one, since the clipboard holds a still image.</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        var frame = Frame;
        var data = Animation is { } animation ? await Task.Run(() => animation.EncodePng(frame)) ?? Data : Data;
        await _services.Platform.SetClipboardImageAsync(data);
        IsCopied = true;
        _copied.Restart(TabViewModel.CopiedFor, () => IsCopied = false);
    }

    /// <summary><b>Save as…</b>: the image as it came, so an animated one stays animated.</summary>
    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var extension = Path.GetExtension(Image.FileName).TrimStart('.');
        if (await _services.Platform.PickSaveFileAsync("Save image", Image.FileName, [new SaveFileType($"{TypeName} image", extension)]) is not { } path)
        {
            return;
        }
        try
        {
            await Task.Run(() => AtomicFile.WriteAllBytes(path, Data));
            Problem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = $"Couldn't save the image: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task OpenAsync()
    {
        if (Image.FilePath is { } path)
        {
            return _services.Platform.OpenFileAsync(path);
        }
        return IsWebImage ? _services.Platform.OpenUrlAsync(Image.Url!.AbsoluteUri) : Task.CompletedTask;
    }

    /// <summary><b>Pause</b>, or <b>Play</b>: on from the frame showing, or from the start once it has played out.</summary>
    [RelayCommand]
    private void PlayPause()
    {
        if (IsPlaying)
        {
            _nextFrame.Cancel();
            IsPlaying = false;
        }
        else
        {
            Play();
        }
    }

    private void Play()
    {
        if (Animation is not { } animation)
        {
            return;
        }
        if (HasPlayedOut(animation))
        {
            Frame = 0;
            _plays = 0;
        }
        IsPlaying = true;
        _nextFrame.Restart(animation.Duration(Frame), Advance);
    }

    /// <summary>The next frame, after the last the first again, until it has played as many times as it says.</summary>
    private void Advance()
    {
        if (Animation is not { } animation)
        {
            return;
        }
        if (Frame + 1 < animation.FrameCount)
        {
            Frame++;
        }
        else
        {
            _plays++;
            if (HasPlayedOut(animation))
            {
                IsPlaying = false;
                return;
            }
            Frame = 0;
        }
        _nextFrame.Restart(animation.Duration(Frame), Advance);
    }

    /// <summary>It stopped on its last frame after its last time through.</summary>
    private bool HasPlayedOut(ImageAnimation animation) => animation.Repetitions >= 0 && _plays > animation.Repetitions;

    /// <summary>The window closed.</summary>
    public void Dispose()
    {
        _copied.Dispose();
        _nextFrame.Dispose();
        Animation?.Dispose();
    }
}
