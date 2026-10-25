using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using Claudette.Core.Settings;

namespace Claudette.App.ViewModels;

/// <summary>
/// The image window (DESIGN.md §18, "Images from Claude's tools", "Opening images"), for every tab and Files tab: an
/// image from a tool's result or a reply, and an image file or web image clicked, as Settings → Files and diffs → Open images in
/// says.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Set by the view: shows an image in a window of its own, which disposes the viewer when it closes.</summary>
    public Action<ImageViewerViewModel>? ShowImageWindow { get; set; }

    /// <summary>Settings → Files and diffs → Open images in is Claudette's image viewer.</summary>
    internal bool OpensImages => _services.Settings.Editor.OpenImagesIn == OpenImagesIn.Viewer;

    /// <summary>
    /// A web link clicked in Markdown (DESIGN.md §18, "Opening images"). One whose address ends in an image's extension,
    /// while Settings → Files and diffs → Open images in is the viewer, loads as a web image in Markdown does, through
    /// Claudette's own client and remembered in <paramref name="images"/>, then shows in the image window. What isn't an
    /// image there (a page, an error, more than 20 MB), and any other link, opens in the browser.
    /// </summary>
    internal async Task OpenWebLinkAsync(Uri uri, ReplyImages images)
    {
        var url = uri.OriginalString;
        if (!OpensImages || uri.Scheme is not ("http" or "https") || !ViewerImages.HasExtension(uri.AbsolutePath))
        {
            await _services.Platform.OpenUrlAsync(url);
            return;
        }
        MessageImage image;
        try
        {
            image = images.Loaded(url) ?? await images.LoadAsync(images.Classify(url));
        }
        catch (ReplyImageException)
        {
            await _services.Platform.OpenUrlAsync(url);
            return;
        }
        var name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        ShowImage(new ToolImage(image, uri.Host, name, ToolImages.SaveName(name, image.MediaType), null, null, uri));
    }

    /// <summary>Shows <paramref name="image"/> in a window of its own.</summary>
    internal void ShowImage(ToolImage image)
    {
        var viewer = new ImageViewerViewModel(image, _services);
        if (ShowImageWindow is { } show)
        {
            show(viewer);
        }
        else
        {
            viewer.Dispose();
        }
    }

    /// <summary>
    /// The image file at <paramref name="path"/>, to show in the window when <see cref="OpensImages"/>: one
    /// <see cref="ViewerImages"/> shows, named for its file, and from <paramref name="shownPath"/> (where it is, as the
    /// window's details say it). Null when it isn't one, isn't there or is too large, so it opens as any other file does.
    /// Reads the file: call it off the UI thread.
    /// </summary>
    internal ToolImage? ImageToView(string path, string shownPath) =>
        OpensImages ? ImageFile(path, shownPath) : null;

    /// <summary>The image file at <paramref name="path"/>, whatever the setting says; null when it isn't one the window shows.</summary>
    internal static ToolImage? ImageFile(string path, string shownPath)
    {
        if (ViewerImages.ReadFile(path) is not { } image)
        {
            return null;
        }
        var name = Path.GetFileName(path);
        return new ToolImage(image, shownPath, name, ToolImages.SaveName(name, image.MediaType), path, null);
    }
}
