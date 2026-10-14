using Claudette.App.Services;
using Claudette.Core.Files;
using Claudette.Core.ProjectTools;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// An image from a tool's result, full size in a window of its own (DESIGN.md §18, "Images from Claude's tools"), with
/// <b>Copy image</b>, <b>Save as…</b> and, for an image file Claude read, <b>Open</b>.
/// </summary>
public sealed partial class ImageViewerViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly UiTimeout _copied;

    public ImageViewerViewModel(ToolImage image, AppServices services)
    {
        Image = image;
        _services = services;
        _copied = new UiTimeout(services.Time, services.Dispatcher);
        Details = string.Join(" · ", new[]
        {
            image.Tool,
            $"{TypeName} {FolderCleaner.FormatSize(image.Image.Data.LongLength)}",
            image.ScaledFrom is { } original ? $"scaled down from {original} to send" : null,
        }.OfType<string>());
    }

    public ToolImage Image { get; }

    /// <summary>The window's title: the file's name, or "report.pdf, page 3".</summary>
    public string Title => Image.Name;

    /// <summary>The image's bytes, which the view decodes.</summary>
    public byte[] Data => Image.Image.Data;

    /// <summary>"Read · PNG 12 KB · scaled down from 3000 × 1000 to send".</summary>
    public string Details { get; }

    /// <summary>The file Claude read it from, which can be opened in the app the OS uses for it.</summary>
    public bool CanOpen => Image.FilePath is not null;

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
        _ => "PNG",
    };

    [RelayCommand]
    private async Task CopyAsync()
    {
        await _services.Platform.SetClipboardImageAsync(Data);
        IsCopied = true;
        _copied.Restart(TabViewModel.CopiedFor, () => IsCopied = false);
    }

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
    private Task OpenAsync() => Image.FilePath is { } path ? _services.Platform.OpenFileAsync(path) : Task.CompletedTask;

    /// <summary>The window closed.</summary>
    public void Dispose() => _copied.Dispose();
}
