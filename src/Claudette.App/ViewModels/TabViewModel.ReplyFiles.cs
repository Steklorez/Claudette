using Claudette.App.Conversation;
using Claudette.Core.Protocol;

namespace Claudette.App.ViewModels;

/// <summary>Files named in Claude's replies (DESIGN.md §18): finding them, and a click on one.</summary>
public sealed partial class TabViewModel
{
    private ReplyFiles? _replyFiles;

    /// <summary>The files the tab's replies name: which inline code is a file, and where a clicked link's file is.</summary>
    public ReplyFiles ReplyFiles => _replyFiles ??= new ReplyFiles(
        new ReplyFileFinder(() => Folder, () => ExtraFolders, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), () => FileIndex),
        () => _conversation.FilePaths);

    /// <summary>
    /// A click on a file a reply names. A file opens in the app the OS uses for it and a folder in Finder or Explorer;
    /// a program, script or shortcut shows in its folder instead, since a reply could be prompt-injected to point at one
    /// under an innocent name. A name that matches several files offers them in a menu at the click, and one that matches
    /// none says so.
    /// </summary>
    private async Task OpenFileReferenceAsync(ReplyFileReference reference, object? anchor)
    {
        IReadOnlyList<string> found;
        try
        {
            found = await ReplyFiles.FindAsync(reference, clicked: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            found = [];
        }
        if (found.Count == 0)
        {
            _conversation.AddNote($"Couldn't find {reference.Path}.", NoteKind.Warning);
            return;
        }
        var path = found[0];
        if (found.Count > 1)
        {
            if (await _services.Platform.ChooseAsync(anchor, [.. found.Select(ShownPath)]) is not { } chosen || chosen < 0 || chosen >= found.Count)
            {
                return;
            }
            path = found[chosen];
        }
        var (exists, isFolder, isExecutable) = await Task.Run(() => Inspect(path));
        if (!exists)
        {
            _conversation.AddNote($"Couldn't find {reference.Path}.", NoteKind.Warning);
        }
        else if (ReplyFileReference.IsProgram(path, isExecutable))
        {
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } parent)
            {
                await _services.Platform.RevealFolderAsync(parent);
            }
        }
        else if (isFolder)
        {
            await _services.Platform.RevealFolderAsync(path);
        }
        else
        {
            await _services.Platform.OpenFileAsync(path);
        }
    }

    /// <summary>A file in the menu of several: inside the tab's folder from there, else in full.</summary>
    private string ShownPath(string path)
    {
        var relative = Path.GetRelativePath(Folder, path);
        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    /// <summary>Whether it's there, a folder, and (on macOS and Linux) a file with its execute bit set.</summary>
    private static (bool Exists, bool IsFolder, bool IsExecutable) Inspect(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return (true, true, false);
            }
            if (!File.Exists(path))
            {
                return (false, false, false);
            }
            const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (true, false, !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & Execute) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, false, false);
        }
    }
}
