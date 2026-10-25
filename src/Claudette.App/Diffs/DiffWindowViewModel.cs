using Claudette.App.Services;
using Claudette.App.ViewModels;
using Claudette.Core.Diffs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.Diffs;

/// <summary>One side of a diff line: its number, text and syntax colors.</summary>
public sealed record DiffCell(string Number, string Text, IReadOnlyList<ColoredRun>? Runs, DiffOp Op)
{
    public bool IsAdded => Op == DiffOp.Added;

    public bool IsRemoved => Op == DiffOp.Removed;
}

/// <summary>A row of the inline diff.</summary>
/// <param name="Hunk">On a hunk's header row, the hunk, which <b>Revert</b> undoes (DESIGN.md §8, "Reverting").</param>
public sealed record InlineDiffRow(string OldNumber, string NewNumber, string Marker, string Text, IReadOnlyList<ColoredRun>? Runs, DiffOp Op, bool IsHeader, DiffHunk? Hunk = null)
{
    public bool CanRevert => Hunk is not null;

    public bool IsAdded => Op == DiffOp.Added && !IsHeader;

    public bool IsRemoved => Op == DiffOp.Removed && !IsHeader;

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => IsHeader || string.IsNullOrWhiteSpace(Marker) ? Text : $"{Marker} {Text}";
}

/// <summary>A row of the side-by-side diff; an empty cell is a line only the other side has.</summary>
public sealed record SideBySideDiffRow(DiffCell? Left, DiffCell? Right, bool IsHeader, string HeaderText, DiffHunk? Hunk = null)
{
    public bool CanRevert => Hunk is not null;

    public bool HasLeft => Left is not null;

    public bool HasRight => Right is not null;

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => IsHeader ? HeaderText : (Left, Right) switch
    {
        ({ } left, { } right) when left.Text != right.Text => $"{left.Text} → {right.Text}",
        ({ } left, _) => left.Text,
        (_, { } right) => right.Text,
        _ => "",
    };
}

/// <summary>What the diff view compares, and what it can do with the file.</summary>
/// <param name="OpenInEditor">
/// <b>Open</b>: true when the file opened in Claudette's window, such as in the project's Files tab, which the diff
/// window stays over, so it closes.
/// </param>
/// <param name="BeforeKnown">
/// False when there's nothing to compare with: what the file held before Claude's first change isn't known (DESIGN.md
/// §8, "Before content"). The view shows the whole file as it is now, with nothing marked as changed.
/// </param>
/// <param name="Review">Marking the file as reviewed from the view, or null when it can't be.</param>
/// <param name="FromGit">
/// <paramref name="Before"/> is the file at HEAD (Changed files in git mode), not before Claude's first change. No HEAD
/// copy means the file is new to git, maybe the user's own, so it isn't deleted: there's nothing to revert to.
/// </param>
/// <param name="After">
/// What to compare with instead of the file now: how an earlier turn left it (DESIGN.md §8, "Changes per turn"). Null
/// reads the file.
/// </param>
/// <param name="AllowRevert">Whether <b>Revert</b> is offered; a turn's changes are only shown.</param>
/// <param name="OpenInSystem"><b>Open in the OS's app</b>, whatever Settings → Files and diffs says; null leaves it out.</param>
/// <param name="RevertInto">
/// Where <b>Revert</b> puts the text back instead of writing the file: the Project Files tab's editor (DESIGN.md §18,
/// "Project files"), which compares with its own text. False when it couldn't.
/// </param>
/// <param name="WordWrap"><b>Wrap lines</b> as the last diff was left, kept for the next; null starts it off.</param>
/// <param name="AgainstNow">
/// For a file that's changed since the session's latest change to it, which this diff ends at (DESIGN.md §8, "This
/// session's changes"): the same "before" against the file now, which <b>With later changes</b> switches to.
/// </param>
/// <param name="Hidden">
/// What changed that the lines don't show (DESIGN.md §8, "What the lines don't show"), given the text the view compares
/// with: <paramref name="Before"/> may not have the line endings the file had. Null says nothing.
/// </param>
public sealed record DiffSource(
    string Path,
    string DisplayPath,
    string? Before,
    string BeforeLabel,
    Func<Task>? OpenInDiffTool,
    Func<Task<bool>> OpenInEditor,
    Func<Task> Reveal,
    Func<Task> CopyPath,
    bool BeforeKnown = true,
    DiffReview? Review = null,
    bool FromGit = false,
    DiffAfter? After = null,
    bool AllowRevert = true,
    Func<string?, bool>? RevertInto = null,
    Func<Task>? OpenInSystem = null,
    DiffWordWrap? WordWrap = null,
    DiffSource? AgainstNow = null,
    Func<string?, HiddenChanges?>? Hidden = null);

/// <summary>A diff's "after" side that isn't the file now; null text means the file didn't exist then.</summary>
public sealed record DiffAfter(string? Text);

/// <summary>
/// The diff view's <b>Wrap lines</b> (DESIGN.md §8): on to begin with when the last diff, in its window or a Files tab,
/// was left with it on, and remembering how this one is left.
/// </summary>
public sealed record DiffWordWrap(bool IsOn, Action<bool> Remember)
{
    /// <summary>As the app state keeps it.</summary>
    public static DiffWordWrap From(AppServices services) => new(services.State.DiffWordWrap, wrap =>
    {
        if (services.State.DiffWordWrap != wrap)
        {
            services.State.DiffWordWrap = wrap;
            services.SaveState();
        }
    });
}

/// <summary>The diff view's <b>Reviewed</b> button (DESIGN.md §8, "Reviewed"). Called on the UI thread.</summary>
/// <param name="LatestChange">The id of Claude's latest change to the file, or null when it hasn't changed it.</param>
/// <param name="IsReviewed">Whether the file is marked as reviewed now.</param>
/// <param name="MarkReviewed">Marks the file as reviewed as of the given change: the one the view showed.</param>
public sealed record DiffReview(Func<string?> LatestChange, Func<bool> IsReviewed, Action<string?> MarkReviewed);

/// <summary>
/// The built-in diff view (DESIGN.md §8): the file before Claude's first change (or at HEAD) against the file now,
/// inline or side by side, with syntax highlighting.
/// </summary>
public sealed partial class DiffWindowViewModel : ViewModelBase
{
    private readonly DiffSource _original;
    private bool _dark;
    private string? _after;
    /// <summary>Claude's latest change to the file when it was read: what <b>Reviewed</b> marks.</summary>
    private string? _shownChange;
    private IReadOnlyList<IReadOnlyList<ColoredRun>>? _beforeColors;
    private IReadOnlyList<IReadOnlyList<ColoredRun>>? _afterColors;
    private bool _isBinary;
    /// <summary>What changed that the lines don't show, as the source says for the file read.</summary>
    private HiddenChanges? _hidden;
    private bool _noLineChanged;
    /// <summary>The changed lines whose only change is whitespace, which shows on them.</summary>
    private HashSet<DiffLineEntry> _whitespace = [];

    public DiffWindowViewModel(DiffSource source, bool dark)
    {
        _original = source;
        _dark = dark;
        ShowWholeFile = !source.BeforeKnown;
        WordWrap = source.WordWrap?.IsOn ?? false;
        _ = LoadAsync();
    }

    /// <summary>What's compared: the diff it was opened with, or with <b>With later changes</b>, the file now.</summary>
    private DiffSource Source => ShowLaterChanges && _original.AgainstNow is { } now ? now : _original;

    /// <summary>The file changed since the session's latest change to it, which the diff ends at: <b>With later changes</b> is offered.</summary>
    public bool HasLaterChanges => _original.AgainstNow is not null;

    /// <summary><b>With later changes</b>: the file now, with what changed since the session's latest change, instead of as that change left it.</summary>
    [ObservableProperty]
    public partial bool ShowLaterChanges { get; set; }

    partial void OnShowLaterChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(BeforeLabel));
        OnPropertyChanged(nameof(CanOpenInDiffTool));
        OnPropertyChanged(nameof(CanMarkReviewed));
        OnPropertyChanged(nameof(CanOpenInSystem));
        OnPropertyChanged(nameof(RevertFileTip));
        RevertConfirmation.Cancel();
        _ = LoadAsync();
    }

    public string Title => Path.GetFileName(Source.Path);

    public string DisplayPath => Source.DisplayPath;

    public string BeforeLabel => Source.BeforeLabel;

    public bool CanOpenInDiffTool => Source.OpenInDiffTool is not null;

    public bool CanMarkReviewed => Source.Review is not null;

    /// <summary>The file is marked as reviewed: the <b>Reviewed</b> button shows a check.</summary>
    [ObservableProperty]
    public partial bool IsReviewed { get; private set; }

    /// <summary>Asks the window to close, after <b>Reviewed</b>.</summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial string Stats { get; set; } = "";

    /// <summary>
    /// Under the path: what changed that the lines don't show, and how whitespace shows on a line that changed only in
    /// it (DESIGN.md §8, "What the lines don't show"). Null when there's nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInline))]
    public partial bool IsSideBySide { get; set; }

    public bool IsInline => !IsSideBySide;

    partial void OnIsSideBySideChanged(bool value) => Build();

    /// <summary>The whole file rather than just the changes with a few lines around them.</summary>
    [ObservableProperty]
    public partial bool ShowWholeFile { get; set; }

    partial void OnShowWholeFileChanged(bool value) => Build();

    /// <summary><b>Wrap lines</b>: long lines wrap at the width shown, rather than scrolling sideways.</summary>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    partial void OnWordWrapChanged(bool value) => Source.WordWrap?.Remember(value);

    [ObservableProperty]
    public partial IReadOnlyList<InlineDiffRow> InlineRows { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SideBySideDiffRow> SideRows { get; set; } = [];

    [RelayCommand]
    private Task OpenInDiffToolAsync() => Source.OpenInDiffTool?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private async Task OpenInEditorAsync()
    {
        if (await Source.OpenInEditor())
        {
            // Claudette's window has the file now, and this one, which stays over it, would hide it.
            CloseRequested?.Invoke();
        }
    }

    public bool CanOpenInSystem => Source.OpenInSystem is not null;

    [RelayCommand]
    private Task OpenInSystemAsync() => Source.OpenInSystem?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task RevealAsync() => Source.Reveal();

    [RelayCommand]
    private Task CopyPathAsync() => Source.CopyPath();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    // ---- Reverting (DESIGN.md §8) --------------------------------------------------------------------------------

    /// <summary>
    /// Claude's changes can be put back: the file as it was before is known, and the view isn't showing a file that's
    /// gone or binary.
    /// </summary>
    public bool CanRevert => HasRevertTarget && !IsLoading && _after is not null && !_isBinary && !string.Equals(_after, Source.Before, StringComparison.Ordinal);

    /// <summary>There's something to put back: what the file held is known, and from git, the file is at HEAD.</summary>
    private bool HasRevertTarget => Source.AllowRevert && (Source.After is null || Source.RevertInto is not null) && Source.BeforeKnown && !(Source.FromGit && Source.Before is null);

    /// <summary><b>Revert file</b>'s tip: what the file goes back to.</summary>
    public string RevertFileTip => Source.FromGit ? "Put the whole file back as it is at HEAD" : "Put the whole file back as it was before Claude changed it";

    /// <summary><b>Revert file</b> asks first: the second step shows in place.</summary>
    public InlineConfirmation RevertConfirmation => field ??= new(RevertFileAsync);

    /// <summary>
    /// Puts the whole file back as it was before Claude's first change; a file Claude created is deleted. From git, it's
    /// the file at HEAD, in the line endings the file has now: git keeps its copy with the ones it was committed in.
    /// </summary>
    [RelayCommand]
    private Task RevertFileAsync() => RevertAsync(Source.FromGit ? Revert.WithLineEndingsOf(Source.Before, _after) : Source.Before);

    /// <summary>Undoes one hunk of the diff, leaving the rest of Claude's changes.</summary>
    [RelayCommand]
    private Task RevertHunkAsync(DiffHunk? hunk)
    {
        if (hunk is null || Revert.Hunk(_after, hunk) is not { } reverted)
        {
            Message = ChangedSince;
            return Task.CompletedTask;
        }
        return RevertAsync(reverted);
    }

    private const string ChangedSince = "The file changed since this was shown, so nothing was reverted. Refresh to see it now.";

    private async Task RevertAsync(string? text)
    {
        if (!CanRevert)
        {
            return;
        }
        if (Source.RevertInto is { } revertInto)
        {
            if (!revertInto(text))
            {
                Message = ChangedSince;
            }
            return;
        }
        var path = Source.Path;
        var shown = _after;
        try
        {
            if (!await Task.Run(() => Revert.Write(path, shown, text)))
            {
                Message = ChangedSince;
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't revert {Path.GetFileName(path)}: {ex.Message}";
            return;
        }
        await LoadAsync();
    }

    /// <summary>
    /// <b>Reviewed</b>: marks the file as reviewed and closes the view (DESIGN.md §8, "Reviewed"). It marks the change the
    /// view showed, so a change Claude made since leaves the file unreviewed.
    /// </summary>
    [RelayCommand]
    private void MarkReviewed()
    {
        if (Source.Review is not { } review)
        {
            return;
        }
        review.MarkReviewed(_shownChange);
        IsReviewed = review.IsReviewed();
        CloseRequested?.Invoke();
    }

    /// <summary>Counts the loads started, so one that finishes after a later one (<b>With later changes</b> switched meanwhile) leaves the later one's.</summary>
    private int _loads;

    private async Task LoadAsync()
    {
        var load = ++_loads;
        IsLoading = true;
        // Before the file is read, so a change Claude makes meanwhile isn't counted as seen.
        _shownChange = Source.Review?.LatestChange();
        IsReviewed = Source.Review?.IsReviewed() ?? false;
        var before = Source.Before;
        var path = Source.Path;
        var dark = _dark;
        var fixedAfter = Source.After;
        var (after, beforeColors, afterColors, message) = await Task.Run(() =>
        {
            string? current = fixedAfter?.Text;
            try
            {
                if (fixedAfter is null)
                {
                    // With its byte order mark, as Claude Code's originalFile has it.
                    current = File.Exists(path) ? TextFiles.ReadAll(path) : null;
                }
            }
            catch (IOException)
            {
            }
            if (IsBinary(before, current))
            {
                return (current, null, null, "This is a binary file, so there's nothing to show line by line.");
            }
            var (beforeColors, afterColors) = Highlight(before, current, path, dark);
            return (current, beforeColors, afterColors, current is not null ? null : fixedAfter is null ? "The file has been deleted." : "The file didn't exist after this turn.");
        });
        if (load != _loads)
        {
            return;
        }
        _after = after;
        _isBinary = IsBinary(before, after);
        _beforeColors = Source.BeforeKnown ? beforeColors : afterColors;
        _afterColors = afterColors;
        var (added, removed) = LineDiff.Count(before, after);
        var hidden = Source.BeforeKnown && !_isBinary ? Source.Hidden?.Invoke(after) : null;
        (_hidden, _noLineChanged) = (hidden, added + removed == 0);
        Stats = !Source.BeforeKnown ? "" : added + removed == 0 && HiddenChangeText.Short(hidden) is { } shown ? shown : $"+{added} −{removed}";
        if (message is null && Source.BeforeKnown && !_isBinary && added + removed == 0 && after is not null && (Source.Hidden is not null || Source.FromGit))
        {
            // Nothing to show line by line: say why (DESIGN.md §8, "What the lines don't show").
            message = HiddenChangeText.Note(hidden, linesChanged: false) is { } note ? $"{note}. The lines are the same on both sides."
                : Source.FromGit ? "The lines are the same as at HEAD. Git counts something else as changed, such as its byte order mark or encoding."
                : "Nothing has changed: the lines are the same on both sides.";
        }
        Message = message;
        // Build the rows before saying it's loaded, so nothing sees "loaded" with no rows.
        BuildRows();
        IsLoading = false;
        OnPropertyChanged(nameof(CanRevert));
        if (dark != _dark)
        {
            // The theme changed while it loaded.
            _ = RecolorAsync();
        }
    }

    /// <summary>
    /// The window's theme changed: the syntax colors follow it, for the text already shown, without reading the file
    /// again (which would count Claude's later changes as seen).
    /// </summary>
    public void UseDarkColors(bool dark)
    {
        if (dark == _dark)
        {
            return;
        }
        _dark = dark;
        if (!IsLoading)
        {
            _ = RecolorAsync();
        }
    }

    private async Task RecolorAsync()
    {
        var dark = _dark;
        var before = Source.Before;
        var after = _after;
        var path = Source.Path;
        var (beforeColors, afterColors) = await Task.Run(() => IsBinary(before, after) ? (null, null) : Highlight(before, after, path, dark));
        // The theme changed again, or the file was read again, meanwhile: that colors it instead.
        if (dark != _dark || IsLoading || !ReferenceEquals(after, _after))
        {
            return;
        }
        _beforeColors = Source.BeforeKnown ? beforeColors : afterColors;
        _afterColors = afterColors;
        Build();
    }

    private static bool IsBinary(string? before, string? after) =>
        (before is not null && LineDiff.LooksBinary(before)) || (after is not null && LineDiff.LooksBinary(after));

    private static (IReadOnlyList<IReadOnlyList<ColoredRun>>? Before, IReadOnlyList<IReadOnlyList<ColoredRun>>? After) Highlight(
        string? before, string? after, string path, bool dark)
    {
        var name = Path.GetFileName(path);
        return (before is null ? null : SyntaxHighlighter.Highlight(LineDiff.SplitLines(before), name, dark),
            after is null ? null : SyntaxHighlighter.Highlight(LineDiff.SplitLines(after), name, dark));
    }

    /// <summary>With the "before" unknown, the file is compared with itself, so nothing shows as changed.</summary>
    private string? Before => Source.BeforeKnown ? Source.Before : _after;

    private void Build()
    {
        if (!IsLoading)
        {
            BuildRows();
        }
    }

    private void BuildRows()
    {
        var before = Before;
        var lines = new List<(DiffLineEntry? Line, string? Header, DiffHunk? Hunk)>();
        if (ShowWholeFile)
        {
            lines.AddRange(LineDiff.Full(before, _after).Select(l => ((DiffLineEntry?)l, (string?)null, (DiffHunk?)null)));
        }
        else
        {
            var revertable = HasRevertTarget && _after is not null && !_isBinary;
            foreach (var hunk in LineDiff.Hunks(before, _after))
            {
                lines.Add((null, hunk.Header, revertable ? hunk : null));
                lines.AddRange(hunk.Lines.Select(l => ((DiffLineEntry?)l, (string?)null, (DiffHunk?)null)));
            }
        }
        _whitespace = WhitespaceOnlyLines(lines.Select(l => l.Line));
        // When no line changed, the message in the lines' place says it.
        var hiddenNote = _noLineChanged ? null : HiddenChangeText.Note(_hidden, linesChanged: _hidden is not { WhitespaceOnly: true });
        Note = (hiddenNote, _whitespace.Count > 0) switch
        {
            (null, false) => null,
            (null, true) => "Where a line changed only in its whitespace, spaces show as · and tabs as →",
            ({ } note, false) => note,
            ({ } note, true) => _hidden is { WhitespaceOnly: true } ? $"{note}: spaces show as · and tabs as →" : $"{note}. Where a line changed only in its whitespace, spaces show as · and tabs as →",
        };

        if (IsSideBySide)
        {
            var rows = new List<SideBySideDiffRow>();
            var run = new List<DiffLineEntry>();
            foreach (var (line, header, hunk) in lines)
            {
                if (line is not null)
                {
                    run.Add(line);
                    continue;
                }
                Flush();
                rows.Add(new SideBySideDiffRow(null, null, true, header!, hunk));
            }
            Flush();
            SideRows = rows;

            void Flush()
            {
                foreach (var pair in LineDiff.SideBySide(run))
                {
                    rows.Add(new SideBySideDiffRow(Cell(pair.Left, left: true), Cell(pair.Right, left: false), false, ""));
                }
                run.Clear();
            }
        }
        else
        {
            InlineRows = lines.Select(item => item.Line is { } l ? InlineRow(l) : new InlineDiffRow("", "", "", item.Header!, null, DiffOp.Context, true, item.Hunk)).ToArray();
        }

        InlineDiffRow InlineRow(DiffLineEntry line)
        {
            var (text, runs) = Shown(line, Colors(line));
            return new InlineDiffRow(line.OldNumber?.ToString() ?? "", line.NewNumber?.ToString() ?? "",
                line.Op switch { DiffOp.Added => "+", DiffOp.Removed => "−", _ => " " }, text, runs, line.Op, false);
        }
    }

    private DiffCell? Cell(DiffLineEntry? line, bool left)
    {
        if (line is null)
        {
            return null;
        }
        var number = left ? line.OldNumber : line.NewNumber;
        var colors = left ? _beforeColors : _afterColors;
        var (text, runs) = Shown(line, number is { } n && colors is not null && n - 1 < colors.Count ? colors[n - 1] : null);
        return new DiffCell(number?.ToString() ?? "", text, runs, line.Op);
    }

    // ---- Whitespace (DESIGN.md §8, "What the lines don't show") -------------------------------------------------------

    /// <summary>How whitespace shows on a line that changed only in it: a muted gray, over both themes' backgrounds.</summary>
    private const string WhitespaceColor = "#A0888888";

    /// <summary>A line's text and colors as shown: with its spaces as · and its tabs as → when it changed only in its whitespace.</summary>
    private (string Text, IReadOnlyList<ColoredRun>? Runs) Shown(DiffLineEntry line, IReadOnlyList<ColoredRun>? runs)
    {
        if (!_whitespace.Contains(line))
        {
            return (line.Text, runs);
        }
        var text = line.Text;
        var colors = new string?[text.Length];
        foreach (var run in runs ?? [])
        {
            for (var i = Math.Max(0, run.Start); i < Math.Min(text.Length, run.Start + run.Length); i++)
            {
                colors[i] = run.Color;
            }
        }
        var shown = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            shown[i] = text[i] switch { '\t' => '→', var c when char.IsWhiteSpace(c) => '·', var c => c };
            if (shown[i] != text[i])
            {
                colors[i] = WhitespaceColor;
            }
        }
        var colored = new List<ColoredRun>();
        for (var i = 0; i < text.Length; i++)
        {
            if (colored.Count > 0 && colored[^1].Color == colors[i])
            {
                colored[^1] = colored[^1] with { Length = colored[^1].Length + 1 };
            }
            else
            {
                colored.Add(new ColoredRun(i, 1, colors[i]));
            }
        }
        return (new string(shown), colored);
    }

    /// <summary>
    /// The changed lines whose only difference from the line they replace is whitespace: in each change, the removed lines
    /// paired in order with the added ones, as side by side pairs them.
    /// </summary>
    private static HashSet<DiffLineEntry> WhitespaceOnlyLines(IEnumerable<DiffLineEntry?> lines)
    {
        var found = new HashSet<DiffLineEntry>();
        var removed = new List<DiffLineEntry>();
        var added = new List<DiffLineEntry>();
        foreach (var line in lines.Append(null))
        {
            if (line is { Op: DiffOp.Removed } && added.Count == 0)
            {
                removed.Add(line);
                continue;
            }
            if (line is { Op: DiffOp.Added })
            {
                added.Add(line);
                continue;
            }
            for (var k = 0; k < Math.Min(removed.Count, added.Count); k++)
            {
                if (LineDiff.LineDiffersOnlyInWhitespace(removed[k].Text, added[k].Text))
                {
                    found.Add(removed[k]);
                    found.Add(added[k]);
                }
            }
            removed.Clear();
            added.Clear();
            if (line is { Op: DiffOp.Removed })
            {
                removed.Add(line);
            }
        }
        return found;
    }

    /// <summary>Removed lines take the old file's colors; added and unchanged lines the new file's.</summary>
    private IReadOnlyList<ColoredRun>? Colors(DiffLineEntry line) => line.Op == DiffOp.Removed
        ? line.OldNumber is { } old && _beforeColors is { } before && old - 1 < before.Count ? before[old - 1] : null
        : line.NewNumber is { } now && _afterColors is { } after && now - 1 < after.Count ? after[now - 1] : null;
}
