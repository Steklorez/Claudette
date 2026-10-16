using Claudette.Core.ProjectFiles;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The folder groups' Project Files tabs (DESIGN.md §18, "Project files"): opening one from its group, closing it,
/// bringing them back at launch, <b>Go to file</b>, and opening a file in the editor from a reply or Changed files.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// The group's <b>Project files</b> button, and its menu's <b>Open project files</b>: opens its Project Files tab, or
    /// selects it when it's open, with its explorer showing.
    /// </summary>
    [RelayCommand]
    private void OpenProjectFiles(TabGroupViewModel? group)
    {
        if (group is not null)
        {
            var files = ProjectFilesFor(group);
            files.ShowExplorer();
            Selected = files;
        }
    }

    /// <summary>A click on a Project Files tab's row: it takes the focus, as a session tab's does.</summary>
    [RelayCommand]
    private void SelectFiles(ProjectFilesViewModel? files)
    {
        if (files is not null)
        {
            Selected = files;
        }
    }

    /// <summary>The group's Project Files tab, opened if it isn't.</summary>
    internal ProjectFilesViewModel ProjectFilesFor(TabGroupViewModel group)
    {
        if (group.Files is { } open)
        {
            return open;
        }
        var files = new ProjectFilesViewModel(_services, this, group, new ProjectFilesState { Folder = group.Folder });
        AddProjectFiles(group, files);
        SaveTabs();
        return files;
    }

    private void AddProjectFiles(TabGroupViewModel group, ProjectFilesViewModel files)
    {
        group.Files = files;
        OpenTabs.Add(files);
    }

    /// <summary>
    /// Closes a Project Files tab, asking first about files with unsaved edits. Its group leaves the sidebar with it when
    /// it has no session tab. False when it was asked to stay open.
    /// </summary>
    internal async Task<bool> CloseProjectFilesAsync(ProjectFilesViewModel files)
    {
        if (!await files.ConfirmCloseAsync())
        {
            return false;
        }
        var group = files.Group;
        var pane = _panes.IndexOf(files);
        if (pane >= 0)
        {
            _panes.RemoveAt(pane);
        }
        OpenTabs.Remove(files);
        group.Files = null;
        if (ReferenceEquals(Selected, files))
        {
            Selected = _panes.Count > 0 ? _panes[Math.Min(pane, _panes.Count - 1)] : group.Tabs.FirstOrDefault() ?? AllTabs.FirstOrDefault();
        }
        else if (pane >= 0)
        {
            UpdatePanes();
        }
        RemoveGroupIfEmpty(group);
        SaveTabs();
        await files.DisposeAsync();
        return true;
    }

    /// <summary>The Project Files tabs saved, each in its group, which is made for it when no tab of its folder came back.</summary>
    private void RestoreProjectFiles()
    {
        var state = _services.State;
        foreach (var saved in state.ProjectFiles.Where(f => f.Folder.Length > 0 && Directory.Exists(f.Folder)).ToArray())
        {
            var group = GroupFor(saved.Folder);
            if (group.Files is null)
            {
                AddProjectFiles(group, new ProjectFilesViewModel(_services, this, group, saved));
            }
        }
        // Unsaved edits no open file has used for a month go.
        var open = Groups.Select(g => g.Files).OfType<ProjectFilesViewModel>().SelectMany(f => f.OpenFiles.Select(o => o.Path)).ToArray();
        _ = Task.Run(() => _services.EditorBackups.DeleteUnused(open));
    }

    /// <summary>The groups in the order they were left in, which a group with only its Project Files tab needs.</summary>
    private void RestoreGroupOrder()
    {
        if (_services.State.GroupOrder is not { Count: > 0 } order)
        {
            return;
        }
        var position = 0;
        foreach (var folder in order)
        {
            if (Groups.FirstOrDefault(g => FolderHistory.SamePath(g.Folder, folder)) is { } group)
            {
                Groups.Move(Groups.IndexOf(group), Math.Min(position, Groups.Count - 1));
                position++;
            }
        }
    }

    /// <summary>
    /// <c>Ctrl/Cmd+P</c>: the focused tab's project's Files tab, with its explorer's box up to find a file by name. From a
    /// conversation, it opens beside it or in its place, as Settings → Editor says.
    /// </summary>
    [RelayCommand]
    private void GoToFile()
    {
        var group = SelectedFiles?.Group ?? (SelectedTab is { } tab ? Groups.FirstOrDefault(g => g.Tabs.Contains(tab)) : Groups.FirstOrDefault());
        if (group is null)
        {
            return;
        }
        var files = ProjectFilesFor(group);
        ShowFromConversation(files);
        files.StartFind();
    }

    /// <summary>
    /// Settings → Editor → Open files in is Claudette's editor, and <paramref name="path"/> is a file it opens: text, not
    /// over 10 MB. A program or shortcut that isn't text still shows in its folder, and a script opens as text.
    /// </summary>
    internal bool OpensInEditor(string path)
    {
        if (_services.Settings.Editor.OpenFilesIn != OpenFilesIn.Editor)
        {
            return false;
        }
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= TextFile.MaxBytes && !IsUnopenable(path) && !LooksBinary(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>An image, an archive, a build product: a NUL byte near the start, without a mark saying it's UTF-16 or UTF-32.</summary>
    private static bool LooksBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var start = new byte[8000];
        var read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        var bytes = start.AsSpan(0, read);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || bytes.StartsWith((ReadOnlySpan<byte>)[0, 0, 0xFE, 0xFF]))
        {
            return false;
        }
        return bytes.IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// A folder a reply names, in a group's folder, with Settings → Editor → Open files in Claudette's editor: it shows in
    /// that project's explorer, expanded and selected. False when it isn't in one, or files open in the OS's app.
    /// </summary>
    internal async Task<bool> RevealInProjectFilesAsync(string folder)
    {
        if (_services.Settings.Editor.OpenFilesIn != OpenFilesIn.Editor
            || Groups.Where(g => IsInside(folder, g.Folder)).OrderByDescending(g => g.Folder.Length).FirstOrDefault() is not { } group)
        {
            return false;
        }
        var files = ProjectFilesFor(group);
        ShowFromConversation(files);
        await files.RevealFolderInTreeAsync(folder);
        return true;
    }

    /// <summary>Programs and shortcuts that aren't text: <c>.exe</c>, <c>.lnk</c>, <c>.app</c> and the like.</summary>
    private static bool IsUnopenable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".lnk" or ".app" or ".msi" or ".dmg" or ".pkg" or ".com" or ".scr" or ".so" or ".dylib";

    /// <summary>
    /// Opens <paramref name="path"/> in the Files tab of the group whose folder holds it (the deepest), or for a file in
    /// none, of <paramref name="from"/>'s group, at <paramref name="line"/> when given (DESIGN.md §18, "Project files",
    /// "Opening files in the editor"). From a conversation the Files tab opens beside it, or in its place, as Settings →
    /// Editor says, and takes the focus. Opened beside it for its first file, its explorer starts hidden.
    /// </summary>
    internal async Task OpenInEditorAsync(string path, int? line, int? column, int? endLine, TabViewModel? from)
    {
        var group = Groups.Where(g => IsInside(path, g.Folder)).OrderByDescending(g => g.Folder.Length).FirstOrDefault()
            ?? (from is null ? null : Groups.FirstOrDefault(g => g.Tabs.Contains(from)))
            ?? SelectedFiles?.Group
            ?? Groups.FirstOrDefault();
        if (group is null)
        {
            return;
        }
        var files = ProjectFilesFor(group);
        if (from is not null)
        {
            var first = files.OpenFiles.Count == 0;
            if (ShowFromConversation(files) && first)
            {
                files.HideExplorerHere();
            }
        }
        else
        {
            Selected = files;
        }
        await files.OpenFileAsync(path, preview: false, line, column, endLine, focus: true);
    }

    /// <summary>
    /// Shows a Files tab from the focused conversation: beside it, as <b>Open beside</b> does, or in its place, as Settings
    /// → Editor says. One already on screen is only focused. True when it opened beside.
    /// </summary>
    private bool ShowFromConversation(ProjectFilesViewModel files)
    {
        if (files.IsShown || SelectedTab is null || _services.Settings.Editor.FilesFromConversation == FilesFromConversation.InPlace)
        {
            Selected = files;
            return false;
        }
        OpenBeside(files);
        return true;
    }

    /// <summary>A session tab from a Files tab (the peek's header): on screen, it takes the focus; else it opens beside.</summary>
    internal void ShowFromFiles(TabViewModel tab)
    {
        if (tab.IsShown)
        {
            Selected = tab;
            return;
        }
        OpenBeside(tab);
    }

    private static bool IsInside(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative != "." && !FolderListing.IsUp(relative) && !Path.IsPathRooted(relative);
    }

    /// <summary>A tab of the group wrote files (a tool call, or its turn ended): its Files tab looks at its open files again.</summary>
    internal void OnTabWroteFiles(TabViewModel tab)
    {
        if (Groups.FirstOrDefault(g => g.Tabs.Contains(tab))?.Files is { } files)
        {
            files.OnTabWroteFiles();
        }
    }

    /// <summary>Claudette came to the front: every Files tab looks at its open files again, as an editor does.</summary>
    internal void OnAppActivated()
    {
        foreach (var files in Groups.Select(g => g.Files).OfType<ProjectFilesViewModel>())
        {
            _ = files.CheckOpenFilesOnDiskAsync();
        }
    }
}
