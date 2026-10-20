using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Claudette.App.Services;

public sealed class AvaloniaPlatformServices(Func<TopLevel?> topLevel) : IPlatformServices
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (topLevel() is not { } top)
        {
            return null;
        }
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title)
    {
        if (topLevel() is not { } top)
        {
            return null;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<SaveFileType> types)
    {
        if (topLevel() is not { } top)
        {
            return null;
        }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = types.Count > 0 ? types[0].Extension : null,
            FileTypeChoices = [.. types.Select(t => new FilePickerFileType(t.Name) { Patterns = [$"*.{t.Extension}"] })],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task OpenUrlAsync(string url)
    {
        if (topLevel() is { } top && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await top.Launcher.LaunchUriAsync(uri);
        }
    }

    public async Task RevealFolderAsync(string path)
    {
        if (topLevel() is { } top)
        {
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (topLevel() is { } top && File.Exists(path))
        {
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    /// <summary>A folder opens as the OS opens it: a macOS package such as an <c>.xcworkspace</c> opens in its app.</summary>
    public async Task OpenPathAsync(string path)
    {
        if (topLevel() is not { } top)
        {
            return;
        }
        if (Directory.Exists(path))
        {
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
        else if (File.Exists(path))
        {
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    public Task<int?> ChooseAsync(object? anchor, IReadOnlyList<string> choices)
    {
        if ((anchor as Control ?? topLevel()) is not { } target || choices.Count == 0)
        {
            return Task.FromResult<int?>(null);
        }
        var chosen = new TaskCompletionSource<int?>();
        var menu = new ContextMenu();
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            // An underscore in a header marks its access key; doubled, it's shown.
            var item = new MenuItem { Header = choices[i].Replace("_", "__", StringComparison.Ordinal) };
            item.Click += (_, _) => chosen.TrySetResult(index);
            menu.Items.Add(item);
        }
        // After a click's own handler, so the pick wins.
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => chosen.TrySetResult(null));
        menu.Open(target);
        return chosen.Task;
    }

    public IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return [.. FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (topLevel()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task SetClipboardImageAsync(byte[] data)
    {
        if (topLevel()?.Clipboard is not { } clipboard)
        {
            return;
        }
        Bitmap bitmap;
        try
        {
            using var stream = new MemoryStream(data);
            bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            // A format Skia can't decode; there's nothing to copy.
            return;
        }
        // Not disposed: the clipboard can hand the bitmap over to another app after this returns.
        await clipboard.SetBitmapAsync(bitmap);
    }

    public async Task<IReadOnlyList<string>> GetClipboardFilesAsync()
    {
        if (topLevel()?.Clipboard is not { } clipboard || await clipboard.TryGetFilesAsync() is not { } items)
        {
            return [];
        }
        return items.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public async Task<string?> GetClipboardTextAsync() =>
        topLevel()?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    /// <remarks>
    /// Avalonia 12 finds the image the way each OS puts one on the clipboard and gives it as a bitmap, which is sent as
    /// PNG, or JPEG when the PNG is too big (DESIGN.md §5, "Attachments").
    /// <list type="bullet">
    /// <item>Windows: <c>image/png</c> or <c>PNG</c>, then <c>CF_DIB</c>, <c>CF_DIBV5</c> or <c>CF_BITMAP</c>, so a
    /// Snipping Tool screenshot works.</item>
    /// <item>macOS: <c>public.png</c>, with <c>public.tiff</c> or <c>public.jpeg</c> converted to PNG when that's all
    /// there is.</item>
    /// <item>Linux (X11): <c>image/png</c> or <c>image/jpeg</c>.</item>
    /// </list>
    /// </remarks>
    public async Task<byte[]?> GetClipboardImageAsync()
    {
        if (topLevel()?.Clipboard is not { } clipboard || await clipboard.TryGetBitmapAsync() is not { } bitmap)
        {
            return null;
        }
        using (bitmap)
        {
            return ImageFiles.Encode(bitmap);
        }
    }
}

/// <summary>
/// Posts to the dispatcher of the UI thread it was made on. It's looked up once, there, so work posted from another
/// thread never asks for <see cref="Dispatcher.UIThread"/> itself: the headless test session resets that between tests,
/// and a late post from one test's background work would otherwise make it the posting thread's, failing the next test.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    public void Post(Action action) => _dispatcher.Post(action);
}
