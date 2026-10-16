using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.Input;

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
        if (await OpenPathAsync(path, reference.Line, reference.Column, reference.EndLine) == OpenedPath.NotFound)
        {
            _conversation.AddNote($"Couldn't find {reference.Path}.", NoteKind.Warning);
        }
    }

    /// <summary>
    /// Opens a file the session named (DESIGN.md §18, "Project files", "Opening files in the editor"): in the project's
    /// Files tab at <paramref name="line"/>, as Settings → Editor says, or else in the app the OS uses. A program or
    /// shortcut that isn't text shows in its folder rather than running, and a folder shows in the explorer when it's in
    /// the project, else in Finder or Explorer.
    /// </summary>
    internal async Task<OpenedPath> OpenPathAsync(string path, int? line = null, int? column = null, int? endLine = null)
    {
        var (exists, isFolder, isExecutable) = await Task.Run(() => Inspect(path));
        if (!exists)
        {
            return OpenedPath.NotFound;
        }
        if (isFolder)
        {
            if (await _shell.RevealInProjectFilesAsync(path))
            {
                return OpenedPath.InClaudette;
            }
            await _services.Platform.RevealFolderAsync(path);
        }
        else if (await Task.Run(() => _shell.OpensInEditor(path)))
        {
            await _shell.OpenInEditorAsync(path, line, column, endLine, this);
            return OpenedPath.InClaudette;
        }
        else if (ReplyFileReference.IsProgram(path, isExecutable))
        {
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } parent)
            {
                await _services.Platform.RevealFolderAsync(parent);
            }
        }
        else
        {
            await _services.Platform.OpenFileAsync(path);
        }
        return OpenedPath.Elsewhere;
    }

    /// <summary>A tool card's <b>Open</b>: the file it worked on, at the first line an edit changed or where a read started.</summary>
    [RelayCommand]
    private async Task OpenToolFileAsync(ToolUseItem? tool)
    {
        if (tool?.FilePath is not { } file)
        {
            return;
        }
        var path = Path.IsPathRooted(file) ? file : Path.Combine(Folder, file);
        if (await OpenPathAsync(path, tool.FileLine) == OpenedPath.NotFound)
        {
            _conversation.AddNote($"Couldn't find {file}.", NoteKind.Warning);
        }
    }

    /// <summary>
    /// A Files tab's peek went to the change this tab made (DESIGN.md §18, "Project files", "Claude's changes"): its tool
    /// call, expanded, with the subagent groups around it open.
    /// </summary>
    internal void ShowToolUse(string toolUseId)
    {
        if (_conversation.FindToolUse(toolUseId) is not { } tool)
        {
            return;
        }
        for (var owner = Agents.OwnerOf(tool.ToolUseId); owner?.Item is { } group; owner = owner.Parent)
        {
            group.IsExpanded = true;
        }
        ScrollTo(tool);
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

/// <summary>Where <see cref="TabViewModel.OpenPathAsync"/> opened a path.</summary>
internal enum OpenedPath
{
    /// <summary>It isn't there.</summary>
    NotFound,

    /// <summary>In Claudette's window: the file in the project's Files tab, or the folder in its explorer.</summary>
    InClaudette,

    /// <summary>In the OS's app for it, or Finder, Explorer or the file manager.</summary>
    Elsewhere,
}
