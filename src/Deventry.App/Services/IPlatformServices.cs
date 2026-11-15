namespace Deventry.App.Services;

/// <summary>OS dialogs, the browser and the clipboard, behind an interface so view models stay testable.</summary>
public interface IPlatformServices
{
    Task<string?> PickFolderAsync(string title);

    /// <param name="types">The kinds of file it offers; any file without.</param>
    Task<string?> PickFileAsync(string title, IReadOnlyList<FileTypeFilter>? types = null);

    /// <summary>
    /// Opens an address in the browser, or the mail app for <c>mailto</c>. Only https, http and mailto
    /// (<see cref="Core.ProjectTools.ProjectLinks.IsAllowed"/>): any other address, a file or an app's own scheme, could
    /// run a program, so it's refused here whoever asks, and nothing a reply, a server or a project's file names gets past
    /// it (DESIGN.md §18, "Links").
    /// </summary>
    Task OpenUrlAsync(string url) => Core.ProjectTools.ProjectLinks.IsAllowed(url) ? OpenUrlAnywayAsync(url) : Task.CompletedTask;

    /// <summary>
    /// Opens any address with the app the OS has for it, which for a file or an app's own scheme can run a program. Only
    /// for an address the user chose to open after being told so: an MCP server's link's <b>Open anyway</b> (DESIGN.md
    /// §7, "MCP servers asking for input"). Everything else opens through <see cref="OpenUrlAsync"/>.
    /// </summary>
    Task OpenUrlAnywayAsync(string url);

    /// <summary>Shows a folder in Finder or Explorer.</summary>
    Task RevealFolderAsync(string path);

    Task SetClipboardTextAsync(string text);

    /// <summary>Puts an image on the clipboard, from its bytes (PNG, JPEG, GIF or WebP), for <b>Copy image</b>.</summary>
    Task SetClipboardImageAsync(byte[] data);

    /// <summary>Files copied in Finder or Explorer, as local paths, for pasting into the composer (DESIGN.md §5).</summary>
    Task<IReadOnlyList<string>> GetClipboardFilesAsync();

    /// <summary>The clipboard's text, which a paste prefers over an image of the same thing; null when there's none.</summary>
    Task<string?> GetClipboardTextAsync();

    /// <summary>An image on the clipboard, such as a screenshot, as PNG bytes (JPEG when that's too big); null when there's none.</summary>
    Task<byte[]?> GetClipboardImageAsync();

    /// <summary>Opens a file in the app the OS uses for it, for example the user's editor (DESIGN.md §8).</summary>
    Task OpenFileAsync(string path);

    /// <summary>
    /// Opens a file, or a folder the OS treats as a document (a macOS <c>.xcworkspace</c>), in the app the OS uses for
    /// it (DESIGN.md §18, "Project tools").
    /// </summary>
    Task OpenPathAsync(string path) => Directory.Exists(path) ? RevealFolderAsync(path) : OpenFileAsync(path);

    /// <summary>
    /// A menu of <paramref name="choices"/> at the pointer, over <paramref name="anchor"/> (the control clicked), such as
    /// the files a name in a reply matches (DESIGN.md §18, "Files named in Claude's replies"). Returns the index of the
    /// one picked, or null when the menu closed without one.
    /// </summary>
    Task<int?> ChooseAsync(object? anchor, IReadOnlyList<string> choices) => Task.FromResult<int?>(null);

    /// <summary>The font families installed on this machine, for Settings → Appearance. Empty when unknown.</summary>
    IReadOnlyList<string> InstalledFonts() => [];

    /// <summary>
    /// The header can be the window's title bar (DESIGN.md §3, "Window frame"): on Windows and macOS. Linux keeps its window
    /// manager's, which may not draw the window's buttons for a window that takes its title bar.
    /// </summary>
    bool CanMergeTitleBar => !OperatingSystem.IsLinux();

    /// <summary>
    /// The OS's save dialog, offering <paramref name="types"/> (the first is the default) and starting from
    /// <paramref name="suggestedName"/>. Returns the chosen path, or null for Cancel.
    /// </summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<SaveFileType> types);
}

/// <summary>A kind of file the save dialog offers, such as Markdown (<c>md</c>).</summary>
public sealed record SaveFileType(string Name, string Extension);

/// <summary>A kind of file the open dialog offers, such as pictures (<c>*.png</c>, <c>*.ico</c>).</summary>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>Runs work on the UI thread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
