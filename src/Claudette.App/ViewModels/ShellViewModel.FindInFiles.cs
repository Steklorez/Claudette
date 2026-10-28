using Claudette.App.Editor;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Find in files and Replace in files (DESIGN.md §18, "Project files", "Finding in files"): the popup over the window,
/// for the focused tab's project, and what it needs of the Files tabs: their open files' text, and opening a match.
/// </summary>
public sealed partial class ShellViewModel : IFindInFilesHost, IFindResultsHost
{
    /// <summary>The popup, while it's open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFindInFilesOpen))]
    public partial FindInFilesViewModel? FindInFiles { get; private set; }

    public bool IsFindInFilesOpen => FindInFiles is not null;

    /// <summary><c>Ctrl/Cmd+Shift+F</c>, or the palette's <b>Find in files…</b>.</summary>
    [RelayCommand]
    private void OpenFindInFiles() => ShowFindInFiles(replace: false);

    /// <summary><c>Ctrl/Cmd+Shift+R</c>, or the palette's <b>Replace in files…</b>.</summary>
    [RelayCommand]
    private void OpenReplaceInFiles() => ShowFindInFiles(replace: true);

    /// <summary>
    /// Opens the popup for <paramref name="group"/>'s project, or the focused tab's: in <paramref name="folder"/> when a
    /// folder's menu named one, and with the text selected in the editor in its box when that's one line.
    /// </summary>
    internal void ShowFindInFiles(bool replace, string? folder = null, TabGroupViewModel? group = null)
    {
        group ??= SelectedFiles?.Group ?? (SelectedTab is { } tab ? Groups.FirstOrDefault(g => g.Tabs.Contains(tab)) : Groups.FirstOrDefault());
        if (group is null)
        {
            return;
        }
        Palette = null;
        var selected = SelectedFiles?.Showing?.SelectionText?.Invoke();
        if (selected is not { Length: > 0 } || selected.Contains('\n', StringComparison.Ordinal) || selected.Contains('\r', StringComparison.Ordinal))
        {
            selected = null;
        }
        FindInFiles?.Dispose();
        FindInFiles = new FindInFilesViewModel(_services, this, this, group.Folder, group.Label, _services.State.FindInFiles, replace, folder, selected, CloseFindInFiles);
    }

    /// <summary>Esc, a click outside it, or a match opened: it goes, and what it had is kept for the next time.</summary>
    public void CloseFindInFiles()
    {
        if (FindInFiles is not { } find)
        {
            return;
        }
        _services.State.FindInFiles = find.Remember();
        _services.SaveState();
        FindInFiles = null;
        find.Dispose();
    }

    /// <summary>Paths compare as Find in files compares them, as the OS does (its static class's name is the popup's here).</summary>
    private static StringComparer FindPaths => Core.ProjectFiles.FindInFiles.PathComparer;

    private IEnumerable<ProjectFileViewModel> OpenProjectFiles() =>
        Groups.Select(g => g.Files).OfType<ProjectFilesViewModel>().SelectMany(f => f.OpenFiles);

    IReadOnlyDictionary<string, string> IFindInFilesHost.OpenTexts()
    {
        var texts = new Dictionary<string, string>(FindPaths);
        foreach (var file in OpenProjectFiles())
        {
            if (file.Document is { } document)
            {
                texts[file.Path] = document.Text;
            }
        }
        return texts;
    }

    string? IFindInFilesHost.OpenText(string path) =>
        OpenProjectFiles().FirstOrDefault(f => f.Document is not null && FindPaths.Equals(f.Path, path))?.Document!.Text;

    IEditorDocument? IFindInFilesHost.OpenDocument(string path) =>
        OpenProjectFiles().FirstOrDefault(f => f.Document is not null && !f.IsReadOnly && FindPaths.Equals(f.Path, path))?.Document;

    /// <summary>A match, in its project's Files tab with the match selected, as a file clicked in a conversation opens.</summary>
    Task IFindInFilesHost.OpenHitAsync(FindHit hit) =>
        OpenInEditorAsync(hit.Path, hit.Line, hit.Column, hit.Line, SelectedTab, endColumn: hit.Column + hit.Length);

    // ---- Keeping results (DESIGN.md §18, "Finding in files") ------------------------------------------------------------

    /// <summary>The Find window keeps results once tool windows are there to hold it; until then, the popup's results go with it.</summary>
    public bool CanKeepResults => false;

    public void Keep(FindResultsViewModel results) => results.Dispose();
}
