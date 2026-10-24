using Claudette.App.Conversation;
using Claudette.App.Diffs;
using Claudette.App.Editor;
using Claudette.App.Services;
using Claudette.Core.ProjectFiles;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>What the banner over an open file says, while it says anything.</summary>
public enum FileBanner
{
    None,

    /// <summary>The file changed on disk under edits not saved yet.</summary>
    Changed,

    /// <summary>The file is gone from disk.</summary>
    Deleted,
}

/// <summary>A choice of indentation in the chip's menu.</summary>
public sealed record IndentChoice(string Label, bool UseTabs, int Size, bool IsCurrent);

/// <summary>
/// One open file in a Project Files tab (DESIGN.md §18, "Project files"): its text and how it's encoded, its tab, the
/// chip, saving, changes on disk, and Claude's changes to it with the peek and the diff modes.
/// </summary>
public sealed partial class ProjectFileViewModel : ViewModelBase, IDisposable
{
    /// <summary>How long after typing stops the changes are worked out again.</summary>
    internal static readonly TimeSpan MarkersDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>How long after typing stops unsaved text is kept on disk.</summary>
    internal static readonly TimeSpan BackupDelay = TimeSpan.FromSeconds(1);

    private readonly AppServices _services;
    private readonly ProjectFilesViewModel _owner;
    private readonly UiTimeout _markersWait;
    private readonly UiTimeout _backupWait;
    private readonly UiTimeout _diffWait;
    private int _markersGeneration;

    /// <summary>The file as last read or saved: what the disk is compared with, and what a save expects to find.</summary>
    private string? _readText;

    /// <summary>The file on disk when the banner asked about it.</summary>
    private string? _diskText;

    /// <summary>A place to go to that came before the view was there to go to it.</summary>
    private (int Line, int Column, int? EndLine)? _pendingReveal;

    internal ProjectFileViewModel(AppServices services, ProjectFilesViewModel owner, OpenFileState state)
    {
        _services = services;
        _owner = owner;
        Path = state.Path;
        IsPinned = state.IsPinned;
        IsPreview = state.IsPreview && !state.IsPinned;
        CaretLine = Math.Max(1, state.Line);
        CaretColumn = Math.Max(1, state.Column);
        Scroll = state.Scroll;
        Mode = state.Mode;
        _markersWait = new UiTimeout(services.Time, services.Dispatcher);
        _backupWait = new UiTimeout(services.Time, services.Dispatcher);
        _diffWait = new UiTimeout(services.Time, services.Dispatcher);
        TabLabel = Name;
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The path from the project's folder, with forward slashes, or the whole path for a file outside it.</summary>
    public string RelativePath => FolderListing.Relative(_owner.Folder, Path);

    /// <summary>A file in no group's folder, opened here: its tab says its whole path, and the explorer doesn't list it.</summary>
    public bool IsOutsideProject => System.IO.Path.IsPathRooted(RelativePath);

    /// <summary>The tab's label: the name, with its parent folder too when two open files share a name.</summary>
    [ObservableProperty]
    public partial string TabLabel { get; set; }

    /// <summary>The tab's tip: where the file is, and who changed it.</summary>
    public string Tip => Change is { } change ? $"{RelativePath}\nChanged by Claude in {change.AuthorNames}" : RelativePath;

    // ---- Loading -------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(HasProblem), nameof(ProblemText), nameof(CanOpenInViewer))]
    public partial TextFileProblem Problem { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(HasProblem), nameof(ProblemText), nameof(CanOpenInViewer))]
    public partial bool IsLoaded { get; private set; }

    public bool IsText => IsLoaded && Document is not null;

    public bool HasProblem => IsLoaded && Document is null;

    public string ProblemText => Problem switch
    {
        TextFileProblem.Binary => "This file isn't text, so it doesn't open here.",
        TextFileProblem.TooLarge => "This file is over 10 MB, too large to open here.",
        TextFileProblem.Missing => "This file isn't there any more.",
        TextFileProblem.Unreadable => "This file couldn't be read: another program may have it open, or Claudette isn't allowed to.",
        _ => "",
    };

    /// <summary>
    /// It didn't open here and is an image the image window may show, so the note offers <b>Open in Claudette's image
    /// viewer</b> (DESIGN.md §18, "Opening images"). Its bytes are checked on the click.
    /// </summary>
    public bool CanOpenInViewer => HasProblem && Problem is TextFileProblem.Binary or TextFileProblem.TooLarge && ViewerImages.HasExtension(Path);

    /// <summary>The file's text, once it's read and is text.</summary>
    public IEditorDocument? Document { get; private set; }

    /// <summary>Why it opens read-only (not valid text, too long a line, not allowed to write it), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    public partial string? ReadOnlyReason { get; private set; }

    public bool IsReadOnly => ReadOnlyReason is not null;


    [ObservableProperty]
    public partial string EncodingLabel { get; private set; } = "UTF-8";

    /// <summary>The grammar that colors it, and its name on the chip.</summary>
    [ObservableProperty]
    public partial EditorLanguage Language { get; private set; } = EditorLanguage.PlainText;

    /// <summary>Reads the file, and its unsaved edits from before, if any. Called once, when it's opened.</summary>
    internal async Task LoadAsync()
    {
        var path = Path;
        var (content, backup) = await Task.Run(() => (TextFile.Read(path), _services.EditorBackups.Load(path)));
        if (content.Problem != TextFileProblem.None)
        {
            Problem = content.Problem;
            IsLoaded = true;
            _owner.OnFileLoaded(this);
            return;
        }
        _readText = content.Text;
        _readAt = _services.Time.GetUtcNow();
        EncodingLabel = content.EncodingLabel;
        ReadOnlyReason = content.ReadOnlyReason;
        var document = _services.EditorDocuments(content.Text);
        Document = document;
        Language = EditorLanguages.For(path, document.LineCount > 0 ? document.GetLineText(1) : null);
        LineEnding = LineEndings.Majority(content.Text);
        LineEndingIsMixed = LineEndings.Detect(content.Text) == LineEndingKind.Mixed;
        var settings = _services.Settings.Editor;
        Indent = Indentation.Detect(Lines(document)) ?? new IndentStyle(settings.IndentWithTabs, Math.Clamp(settings.IndentSize, 1, 8));
        // The edits kept from before quitting (DESIGN.md §18, "Project files", "Saving").
        if (backup is not null && !IsReadOnly && !string.Equals(backup.Text, content.Text, StringComparison.Ordinal))
        {
            document.ReplaceAll(backup.Text);
            if (backup.Base != EditorBackups.Hash(content.Text))
            {
                // The file changed on disk since those edits were made.
                _diskText = content.Text;
                Banner = FileBanner.Changed;
            }
        }
        document.Changed += OnTextChanged;
        document.ModifiedChanged += OnModifiedChanged;
        IsLoaded = true;
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(IsModified));
        _owner.OnFileLoaded(this);
        RefreshMarkers();
    }

    private static IEnumerable<string> Lines(IEditorDocument document)
    {
        for (var line = 1; line <= Math.Min(document.LineCount, 1000); line++)
        {
            yield return document.GetLineText(line);
        }
    }

    // ---- Its tab ---------------------------------------------------------------------------------------------------

    /// <summary>Pinned: first in the row, and left open by Close others, Close to the right, Close saved and Close all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinMenuText))]
    public partial bool IsPinned { get; set; }

    public string PinMenuText => IsPinned ? "Unpin" : "Pin";

    /// <summary>The preview tab, with its name in italics: the next file opened from the explorer takes its place.</summary>
    [ObservableProperty]
    public partial bool IsPreview { get; set; }

    /// <summary>The file showing in its tab.</summary>
    [ObservableProperty]
    public partial bool IsShowing { get; set; }

    partial void OnIsShowingChanged(bool value)
    {
        if (value && _pendingReveal is { } pending && RevealRequested is not null)
        {
            _pendingReveal = null;
            RevealRequested(pending.Line, pending.Column, pending.EndLine);
        }
    }

    /// <summary>Edits not saved yet: the tab's dot.</summary>
    public bool IsModified => Document?.IsModified ?? false;

    private void OnModifiedChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsModified));
        _owner.OnFileModifiedChanged(this);
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Typing in a preview keeps it.
        if (IsPreview && IsModified)
        {
            IsPreview = false;
        }
        _markersWait.Restart(MarkersDelay, OnTextSettled);
        _backupWait.Restart(BackupDelay, KeepBackup);
        if (Mode != FileViewMode.Edit)
        {
            _diffWait.Restart(MarkersDelay, BuildDiff);
        }
    }

    /// <summary>Typing stopped for a moment: the marks follow, and the view's folds.</summary>
    private void OnTextSettled()
    {
        RefreshMarkers();
        TextSettled?.Invoke();
    }

    /// <summary>Typing stopped for a moment, for what the view works out again then (the folds).</summary>
    public event Action? TextSettled;

    /// <summary>Settings → Files and diffs → Word wrap; <c>Alt+Z</c> changes it for the file in the view.</summary>
    public bool WordWrap => _owner.WordWrap;

    internal void OnWordWrapChanged() => OnPropertyChanged(nameof(WordWrap));

    /// <summary>Unsaved text is kept on disk; once there's none, its copy goes.</summary>
    private void KeepBackup()
    {
        if (Document is not { } document)
        {
            return;
        }
        var path = Path;
        if (document.IsModified)
        {
            var text = document.Text;
            var baseHash = EditorBackups.Hash(_readText);
            _ = Task.Run(() => _services.EditorBackups.Save(path, text, baseHash));
        }
        else
        {
            _ = Task.Run(() => _services.EditorBackups.Delete(path));
        }
    }

    /// <summary>Keeps the unsaved text now, without waiting for typing to stop: quitting, or a restart into a new build.</summary>
    internal void FlushBackup()
    {
        _backupWait.Cancel();
        if (Document is { IsModified: true } document)
        {
            _services.EditorBackups.Save(Path, document.Text, EditorBackups.Hash(_readText));
        }
    }

    // ---- The chip (DESIGN.md §18, "Project files", "The chip") -------------------------------------------------------

    /// <summary>The cursor, which the view keeps up to date.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial int CaretLine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial int CaretColumn { get; set; }

    public string PositionText => $"Ln {CaretLine}, Col {CaretColumn}";

    /// <summary>How far down it's scrolled, which the view keeps up to date, and comes back to.</summary>
    public double Scroll { get; set; }

    /// <summary>What most of its lines end with, which new lines take.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineEndingLabel), nameof(NewLine))]
    public partial LineEndingKind LineEnding { get; private set; }

    /// <summary>It has both kinds: the chip says <i>Mixed</i>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineEndingLabel))]
    public partial bool LineEndingIsMixed { get; private set; }

    public string LineEndingLabel => LineEndingIsMixed ? "Mixed" : LineEndings.Label(LineEnding == LineEndingKind.None ? (Environment.NewLine == "\r\n" ? LineEndingKind.Crlf : LineEndingKind.Lf) : LineEnding);

    /// <summary>The newline a new line takes.</summary>
    public string NewLine => LineEnding switch
    {
        LineEndingKind.Crlf => "\r\n",
        LineEndingKind.Lf => "\n",
        _ => Environment.NewLine,
    };

    /// <summary>
    /// The chip's <b>LF</b> or <b>CRLF</b>: every line's ending becomes that, as an edit like any other, which Undo takes
    /// back and Save writes.
    /// </summary>
    [RelayCommand]
    private void SetLineEndings(LineEndingKind kind)
    {
        if (Document is not { } document || IsReadOnly || kind is not (LineEndingKind.Lf or LineEndingKind.Crlf))
        {
            return;
        }
        var text = document.Text;
        var converted = LineEndings.Convert(text, kind);
        if (!string.Equals(text, converted, StringComparison.Ordinal))
        {
            document.ReplaceAll(converted);
        }
        LineEnding = kind;
        LineEndingIsMixed = false;
    }

    /// <summary>How Tab and new lines indent in this file while it's open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentChoices))]
    public partial IndentStyle Indent { get; private set; } = new(false, 4);

    /// <summary>The chip's menu: spaces or tabs, and the width.</summary>
    public IReadOnlyList<IndentChoice> IndentChoices =>
    [
        .. new[] { 2, 4, 8 }.Select(size => new IndentChoice($"Indent with {size} spaces", false, size, !Indent.UseTabs && Indent.Size == size)),
        .. new[] { 2, 4, 8 }.Select(size => new IndentChoice($"Indent with tabs, {size} wide", true, size, Indent.UseTabs && Indent.Size == size)),
    ];

    /// <summary>The chip's indentation: for Tab and new lines from now on; the lines already there stay as they are.</summary>
    [RelayCommand]
    private void SetIndent(IndentChoice? choice)
    {
        if (choice is not null)
        {
            Indent = new IndentStyle(choice.UseTabs, choice.Size);
        }
    }

    // ---- Saving (DESIGN.md §18, "Project files", "Saving") -----------------------------------------------------------

    /// <summary>What went wrong with the last save, or null.</summary>
    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    /// <summary>
    /// Writes the text, in the file's encoding, byte order mark and line endings, only if the file is still what was read
    /// or last saved. False when it wasn't written: it changed meanwhile (the banner asks), or couldn't be.
    /// </summary>
    internal async Task<bool> SaveAsync()
    {
        if (Document is not { } document || IsReadOnly)
        {
            return false;
        }
        var text = document.Text;
        var expected = _readText;
        var path = Path;
        SaveError = null;
        try
        {
            if (!await Task.Run(() => TextFile.Save(path, expected, text)))
            {
                // It changed on disk since it was read: the banner asks what to keep.
                _diskText = await Task.Run(() => TextFile.ReadTextOrNull(path));
                Banner = _diskText is null && expected is not null ? FileBanner.Deleted : FileBanner.Changed;
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = $"Couldn't save {Name}: {ex.Message}";
            return false;
        }
        _readText = text;
        _readAt = _services.Time.GetUtcNow();
        // Unchanged since the text was taken: now is what's saved.
        if (string.Equals(document.Text, text, StringComparison.Ordinal))
        {
            document.MarkSaved();
        }
        Banner = FileBanner.None;
        _backupWait.Cancel();
        await Task.Run(() => _services.EditorBackups.Delete(path));
        _owner.OnFileSaved(this);
        return true;
    }

    /// <summary>Throws away the unsaved edits' copy: <b>Don't save</b>.</summary>
    internal void DiscardBackup()
    {
        _backupWait.Cancel();
        var path = Path;
        _ = Task.Run(() => _services.EditorBackups.Delete(path));
    }

    // ---- Changes on disk (DESIGN.md §18, "Project files", "When a file changes on disk") -----------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner), nameof(BannerText), nameof(IsChangedBanner), nameof(IsDeletedBanner))]
    public partial FileBanner Banner { get; private set; }

    public bool HasBanner => Banner != FileBanner.None;

    public bool IsChangedBanner => Banner == FileBanner.Changed;

    public bool IsDeletedBanner => Banner == FileBanner.Deleted;

    /// <summary>
    /// The banner's words: Claude's, when one of the project's tabs changed the file since it was read, else the disk's.
    /// </summary>
    public string BannerText => Banner switch
    {
        FileBanner.Changed when Change is { } change && change.LastChanged >= _readAt => $"Claude changed {Name} in {change.AuthorNames} while you were editing it.",
        FileBanner.Changed => $"{Name} changed on disk while you were editing it.",
        FileBanner.Deleted => $"{Name} is gone from disk. Save to write it back.",
        _ => "",
    };

    /// <summary>When the file was last read or saved: a change of Claude's since then is what the banner is about.</summary>
    private DateTimeOffset _readAt;

    /// <summary>Looks at the file on disk again: it changed there, or might have (a tool call, a turn's end, Claudette in front).</summary>
    internal async Task CheckDiskAsync()
    {
        if (!IsLoaded || Document is not { } document)
        {
            return;
        }
        var path = Path;
        var onDisk = await Task.Run(() => TextFile.ReadTextOrNull(path));
        switch (DiskChange.Decide(_readText, onDisk, document.IsModified))
        {
            case DiskChangeAction.Reload:
                Reload(document, onDisk!);
                break;
            case DiskChangeAction.AskChanged:
                _diskText = onDisk;
                Banner = FileBanner.Changed;
                OnPropertyChanged(nameof(BannerText));
                break;
            case DiskChangeAction.AskDeleted:
                _diskText = null;
                Banner = FileBanner.Deleted;
                break;
        }
    }

    /// <summary>Reads the file again where it was, as one step Undo takes back.</summary>
    private void Reload(IEditorDocument document, string text)
    {
        _readText = text;
        _readAt = _services.Time.GetUtcNow();
        if (!string.Equals(document.Text, text, StringComparison.Ordinal))
        {
            document.ReplaceAll(text);
        }
        document.MarkSaved();
        Banner = FileBanner.None;
        LineEndingIsMixed = LineEndings.Detect(text) == LineEndingKind.Mixed;
        RefreshMarkers();
    }

    /// <summary>
    /// <b>Keep mine</b>: the edits stay, and saving writes over the file on disk without asking again. For a file that's
    /// gone, saving writes it back.
    /// </summary>
    [RelayCommand]
    private void KeepMine()
    {
        _readText = _diskText;
        Banner = FileBanner.None;
    }

    /// <summary><b>Use theirs</b>: the file on disk, read again; Undo can take that back.</summary>
    [RelayCommand]
    private void UseTheirs()
    {
        if (Document is { } document && _diskText is { } disk)
        {
            Reload(document, disk);
            KeepBackup();
        }
    }

    /// <summary><b>Compare</b>: the file on disk against the edits, side by side.</summary>
    [RelayCommand]
    private void Compare()
    {
        _comparingDisk = true;
        Mode = FileViewMode.SideBySide;
        BuildDiff();
    }

    private bool _comparingDisk;

    // ---- Claude's changes (DESIGN.md §18, "Project files", "Claude's changes") ---------------------------------------

    /// <summary>Who changed the file and when, from the project's open tabs; null when none of them did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip), nameof(BannerText), nameof(PeekHeader), nameof(CanReview), nameof(CanShowChange), nameof(ShowChangeTip))]
    public partial ProjectFileChange? Change { get; private set; }

    /// <summary>What the file is compared with: before Claude's first change, or at HEAD.</summary>
    private string? _before;
    private bool _hasBaseline;
    private string _baselineLabel = "";

    /// <summary>
    /// What the file is compared with now: the project's tabs' changes, or git's HEAD. With nothing to compare with,
    /// there are no marks.
    /// </summary>
    internal void SetBaseline(bool hasBaseline, string? before, string label, ProjectFileChange? change)
    {
        var changed = hasBaseline != _hasBaseline || !string.Equals(before, _before, StringComparison.Ordinal);
        _hasBaseline = hasBaseline;
        _before = before;
        _baselineLabel = label;
        Change = change;
        if (changed)
        {
            RefreshMarkers();
            if (Mode != FileViewMode.Edit)
            {
                BuildDiff();
            }
        }
    }

    /// <summary>The gutter's bars, worked out again after each pause in typing; empty with nothing to compare with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(ShowsModeBar), nameof(MarkersTip))]
    public partial ChangeMarkers Markers { get; private set; } = ChangeMarkers.None;

    /// <summary>The file is too long to compare: its tab's tip says why there are no marks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkersTip))]
    public partial bool IsTooLongToCompare { get; private set; }

    public string? MarkersTip => IsTooLongToCompare ? "Too long to show what changed" : null;

    public bool HasChanges => !Markers.IsEmpty;

    /// <summary>The bar for Edit, Inline changes, Side by side and Reviewed: while it has changes, or shows them.</summary>
    public bool ShowsModeBar => HasChanges || Mode != FileViewMode.Edit || Change is not null;

    private void RefreshMarkers() => _ = RefreshMarkersAsync();

    /// <summary>Compares the text with what it's compared with, away from the UI thread; a newer refresh wins.</summary>
    private async Task RefreshMarkersAsync()
    {
        if (Document is not { } document)
        {
            return;
        }
        var generation = ++_markersGeneration;
        if (!_hasBaseline)
        {
            Markers = ChangeMarkers.None;
            IsTooLongToCompare = false;
            Peek = null;
            return;
        }
        var text = document.Text;
        var before = _before;
        ChangeMarkers? markers;
        try
        {
            markers = await Task.Run(() => ChangeMarkers.Compute(before, text));
        }
        catch (Exception)
        {
            // A diff that fails shows no marks rather than none of the file.
            markers = ChangeMarkers.None;
        }
        if (generation != _markersGeneration)
        {
            return;
        }
        Markers = markers ?? ChangeMarkers.None;
        IsTooLongToCompare = markers is null;
        // A peek follows its change, where the view's anchor has it now, and closes once the change is gone.
        if (Peek is { } peek)
        {
            var line = PeekLine?.Invoke() ?? (peek.IsRemovalOnly ? peek.FirstLine - 1 : peek.LastLine);
            Peek = Markers.BlockAt(line) ?? Markers.BlockAt(line + 1);
        }
    }

    /// <summary>What the file shows: the editor, or its changes inline or side by side.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode), nameof(IsInlineMode), nameof(IsSideBySideMode), nameof(ShowsModeBar), nameof(IsDiffMode))]
    public partial FileViewMode Mode { get; set; }

    public bool IsEditMode
    {
        get => Mode == FileViewMode.Edit;
        set
        {
            if (value)
            {
                _comparingDisk = false;
                Mode = FileViewMode.Edit;
            }
        }
    }

    public bool IsInlineMode
    {
        get => Mode == FileViewMode.Inline;
        set
        {
            if (value)
            {
                _comparingDisk = false;
                Mode = FileViewMode.Inline;
            }
        }
    }

    public bool IsSideBySideMode
    {
        get => Mode == FileViewMode.SideBySide;
        set
        {
            if (value)
            {
                _comparingDisk = false;
                Mode = FileViewMode.SideBySide;
            }
        }
    }

    public bool IsDiffMode => Mode != FileViewMode.Edit;

    partial void OnModeChanged(FileViewMode value)
    {
        if (value == FileViewMode.Edit)
        {
            Diff = null;
        }
        else
        {
            BuildDiff();
        }
        _owner.OnFileStateChanged();
    }

    /// <summary>§8's diff view inside the tab, read-only, for Inline changes and Side by side.</summary>
    [ObservableProperty]
    public partial DiffWindowViewModel? Diff { get; private set; }

    private void BuildDiff()
    {
        if (Mode == FileViewMode.Edit || Document is not { } document)
        {
            return;
        }
        var text = document.Text;
        var before = _comparingDisk ? _diskText : _before;
        var known = _comparingDisk || _hasBaseline;
        var label = _comparingDisk ? "on disk, against your edits" : _baselineLabel;
        var wholeFile = Diff?.ShowWholeFile;
        Diff = new DiffWindowViewModel(new DiffSource(Path, RelativePath, before, label, null,
            async () =>
            {
                await _owner.OpenInSystemAsync(Path);
                return false;
            },
            () => _owner.RevealAsync(Path), () => _owner.CopyPathAsync(Path), known,
            After: new DiffAfter(text), AllowRevert: !IsReadOnly && !_comparingDisk, RevertInto: RevertInto,
            WordWrap: DiffWordWrap.From(_services)), _owner.IsDarkTheme)
        {
            IsSideBySide = Mode == FileViewMode.SideBySide,
        };
        if (wholeFile is { } whole)
        {
            Diff.ShowWholeFile = whole;
        }
    }

    /// <summary><b>Revert</b> in the diff modes: changes the editor's text, as the peek's does, rather than writing the file.</summary>
    private bool RevertInto(string? text)
    {
        if (Document is not { } document || text is null || IsReadOnly)
        {
            return false;
        }
        document.ReplaceAll(text);
        return true;
    }

    /// <summary>
    /// A double-click on a line of the diff: <b>Edit</b>, at that line of the file as it is now, or for a line only the
    /// "before" had, at the line after it.
    /// </summary>
    internal void EditAt(object? row)
    {
        var line = (Diff, row) switch
        {
            ({ } diff, InlineDiffRow inline) => FirstNumber(diff.InlineRows, inline, r => r.IsHeader ? null : r.NewNumber),
            ({ } diff, SideBySideDiffRow side) => FirstNumber(diff.SideRows, side, r => r.IsHeader ? null : r.Right?.Number),
            _ => null,
        };
        Mode = FileViewMode.Edit;
        if (line is { } at)
        {
            Reveal(at, 1, null);
        }
    }

    /// <summary>The line number of <paramref name="from"/>, or of the first row after it that has one.</summary>
    private static int? FirstNumber<T>(IReadOnlyList<T> rows, T from, Func<T, string?> number) where T : class
    {
        var start = 0;
        while (start < rows.Count && !ReferenceEquals(rows[start], from))
        {
            start++;
        }
        for (var i = start; i < rows.Count; i++)
        {
            if (int.TryParse(number(rows[i]), System.Globalization.CultureInfo.InvariantCulture, out var line))
            {
                return line;
            }
        }
        return null;
    }

    /// <summary>The theme changed: the diff modes' colors follow.</summary>
    internal void UseDarkColors(bool dark) => Diff?.UseDarkColors(dark);

    // ---- Reviewed --------------------------------------------------------------------------------------------------

    /// <summary>Claude changed it, so it can be ticked as reviewed.</summary>
    public bool CanReview => Change is not null;

    /// <summary>Every open tab of the project that changed it has it ticked.</summary>
    [ObservableProperty]
    public partial bool IsReviewed { get; internal set; }

    /// <summary><b>Reviewed</b>, on the bar and the tab's menu: ticks it in every tab of the project that changed it.</summary>
    [RelayCommand]
    private void ToggleReviewed() => _owner.SetReviewed(this, !IsReviewed);

    // ---- The peek ----------------------------------------------------------------------------------------------------

    /// <summary>The change whose old lines show under it, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeekHeader), nameof(PeekOldText), nameof(HasPeek))]
    public partial ChangeBlock? Peek { get; private set; }

    public bool HasPeek => Peek is not null;

    /// <summary>The line the view's peek is under now, which moves with the text; null without a view.</summary>
    internal Func<int?>? PeekLine { get; set; }

    /// <summary>The peek's header: who changed the file, and when.</summary>
    public string PeekHeader => Change is { } change
        ? $"Changed by Claude in {change.AuthorNames} · {MessageTimes.Short(change.LastChanged, _services.Time)}"
        : _baselineLabel.Length > 0 ? $"Changed, {_baselineLabel}" : "Changed";

    /// <summary>The tab that changed the file last, which the peek's header goes to.</summary>
    private ChangeAuthor? LatestAuthor => Change?.Authors.MaxBy(a => a.LastChanged);

    public bool CanShowChange => LatestAuthor is not null;

    public string? ShowChangeTip => LatestAuthor is { } author ? $"Show the change in {author.TabName}" : null;

    /// <summary>A click on the peek's header: the tab that changed the file last, at its tool call.</summary>
    [RelayCommand]
    private void ShowChange()
    {
        if (LatestAuthor is { } author)
        {
            _owner.ShowChangeIn(author);
        }
    }

    /// <summary>The lines the change replaced.</summary>
    public string PeekOldText => Peek is { } peek ? string.Join("\n", peek.OldLines) : "";

    /// <summary>A click on a bar: the old lines under its change, or closes the peek that's open on it.</summary>
    internal void TogglePeekAt(int line)
    {
        var block = Markers.BlockAt(line);
        Peek = block is null || ReferenceEquals(block, Peek) || Peek is { } open && open.FirstLine == block.FirstLine ? null : block;
    }

    [RelayCommand]
    private void ClosePeek() => Peek = null;

    /// <summary>F4, or the peek's ↓: the next change, with its peek open.</summary>
    [RelayCommand]
    private void NextChange() => GoToChange(Markers.Next(Peek?.LastLine ?? CaretLine));

    /// <summary>F3, or the peek's ↑: the change before, with its peek open.</summary>
    [RelayCommand]
    private void PreviousChange() => GoToChange(Markers.Previous(Peek?.FirstLine ?? CaretLine));

    private void GoToChange(ChangeBlock? block)
    {
        if (block is null)
        {
            return;
        }
        Peek = block;
        Reveal(block.FirstLine, 1, null);
    }

    /// <summary>
    /// The peek's <b>Revert</b>: the lines the change replaced go back in the editor's text, as one step Undo takes back.
    /// Nothing is written until it's saved.
    /// </summary>
    [RelayCommand]
    private void RevertPeek()
    {
        if (Peek is not { } peek || Document is not { } document || IsReadOnly)
        {
            return;
        }
        document.ReplaceLines(peek.FirstLine, peek.LineCount, peek.OldLines, NewLine);
        Peek = null;
    }

    // ---- What the view is asked to do --------------------------------------------------------------------------------

    /// <summary>Go to a line and column, and select to the end of <c>EndLine</c> when there's one.</summary>
    public event Action<int, int, int?>? RevealRequested;

    /// <summary>Open the editor's find, or its replace.</summary>
    public event Action<bool>? FindRequested;

    /// <summary>Open Go to line.</summary>
    public event Action? GoToLineRequested;

    /// <summary>Put the keyboard in the editor.</summary>
    public event Action? FocusRequested;

    /// <summary>
    /// Goes to <paramref name="line"/>, and selects through <paramref name="endLine"/> when it's given. If the view isn't
    /// there yet, it goes there once it is.
    /// </summary>
    internal void Reveal(int line, int column, int? endLine)
    {
        CaretLine = Math.Max(1, line);
        CaretColumn = Math.Max(1, column);
        if (RevealRequested is null || !IsShowing)
        {
            _pendingReveal = (line, column, endLine);
            return;
        }
        RevealRequested(line, column, endLine);
    }

    /// <summary>The view takes any place it was asked to go before it was there.</summary>
    internal (int Line, int Column, int? EndLine)? TakePendingReveal()
    {
        var pending = _pendingReveal;
        _pendingReveal = null;
        return pending;
    }

    internal void RequestFind(bool replace) => FindRequested?.Invoke(replace);

    internal void RequestGoToLine() => GoToLineRequested?.Invoke();

    internal void RequestFocus() => FocusRequested?.Invoke();

    /// <summary>What's saved for the file's tab.</summary>
    internal OpenFileState CaptureState() => new()
    {
        Path = Path,
        IsPinned = IsPinned,
        IsPreview = IsPreview,
        Line = CaretLine,
        Column = CaretColumn,
        Scroll = Scroll,
        Mode = Mode,
    };

    public void Dispose()
    {
        _markersWait.Dispose();
        _backupWait.Dispose();
        _diffWait.Dispose();
        if (Document is { } document)
        {
            document.Changed -= OnTextChanged;
            document.ModifiedChanged -= OnModifiedChanged;
        }
    }
}
