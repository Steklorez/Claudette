using System.Text.Json;
using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Diffs;

public enum ChangedFileStatus
{
    Added,
    Modified,
    Deleted,
    /// <summary>The file on disk matches its "before" content again (ignoring line endings).</summary>
    Unchanged,
}

/// <summary>A file the session changed with Edit, Write, MultiEdit or NotebookEdit (DESIGN.md §8).</summary>
public sealed class ChangedFile
{
    private readonly List<string> _toolUseIds = [];

    internal ChangedFile(string path, string? before, bool isNew, bool beforeKnown, string toolUseId, DateTimeOffset at, string? after = null, bool afterKnown = false)
    {
        Path = path;
        Before = before;
        IsNew = isNew;
        BeforeKnown = beforeKnown;
        FirstChanged = at;
        LastChanged = at;
        After = after;
        AfterKnown = afterKnown;
        _toolUseIds.Add(toolUseId);
    }

    /// <summary>The path as Claude Code reported it on the first change.</summary>
    public string Path { get; }

    /// <summary>
    /// The content before Claude's first change in the session (its <c>originalFile</c>). Null when the file was new,
    /// or when <see cref="BeforeKnown"/> is false.
    /// </summary>
    public string? Before { get; }

    /// <summary>The file didn't exist before Claude's first change.</summary>
    public bool IsNew { get; }

    /// <summary>
    /// False when the first change's result didn't say what the file held before and nothing was saved for it: a
    /// transcript's result for a file over 10,000 characters, a Write over a file too large for Claude Code to diff, or an
    /// unknown result shape.
    /// </summary>
    public bool BeforeKnown { get; }

    public DateTimeOffset FirstChanged { get; }

    public DateTimeOffset LastChanged { get; private set; }

    /// <summary>
    /// The file as the session's latest change left it (DESIGN.md §8, "This session's changes"), worked out from the
    /// change (<see cref="FileAfter"/>). Null when <see cref="AfterKnown"/> is false.
    /// </summary>
    public string? After { get; private set; }

    /// <summary>
    /// False when what the latest change left isn't known, such as an Edit to a file whose "before" isn't known: the
    /// file's changes are then counted against the disk, as they were before Claudette kept its own "after".
    /// </summary>
    public bool AfterKnown { get; private set; }

    /// <summary>
    /// One of the session's changes found the file other than its previous change left it: another tab, you or a
    /// command changed it between them, so the diff from <see cref="Before"/> to <see cref="After"/> has that in it too.
    /// </summary>
    public bool ChangedBetween { get; private set; }

    public int ChangeCount => _toolUseIds.Count;

    /// <summary>The tool calls that changed the file, in order.</summary>
    public IReadOnlyList<string> ToolUseIds => _toolUseIds;

    internal void AddChange(string toolUseId, DateTimeOffset at, string? after = null, bool afterKnown = false, bool changedBetween = false)
    {
        _toolUseIds.Add(toolUseId);
        if (at > LastChanged)
        {
            LastChanged = at;
        }
        (After, AfterKnown) = (after, afterKnown);
        ChangedBetween |= changedBetween;
    }
}

/// <summary>A file one turn changed (DESIGN.md §8, "Changes per turn"), with what it held before the turn's first change to it.</summary>
/// <param name="Path">The path as Claude Code reported it.</param>
/// <param name="Before">The file before this turn changed it; null when it was new, or when <paramref name="BeforeKnown"/> is false.</param>
/// <param name="IsNew">The turn created it.</param>
/// <param name="After">The file as the turn's last change to it left it; null when <paramref name="AfterKnown"/> is false.</param>
/// <param name="AfterKnown">Whether that's known (<see cref="ChangedFile.AfterKnown"/>).</param>
public sealed record TurnFileChange(string Path, string? Before, bool IsNew, bool BeforeKnown, string? After = null, bool AfterKnown = false);

/// <summary>
/// A changed file compared with the disk now. <paramref name="Added"/> and <paramref name="Removed"/> are null when
/// the lines can't be counted: a binary or very large file, or an unknown "before".
/// </summary>
public sealed record ChangedFileState(ChangedFileStatus Status, int? Added, int? Removed, string? Current)
{
    public bool IsBinary { get; init; }

    public bool IsTooLarge { get; init; }

    /// <summary>
    /// The file on disk isn't as the session's latest change left it: what changed since, or null when it is, or when
    /// that can't be told (DESIGN.md §8, "This session's changes").
    /// </summary>
    public ChangedSince? Since { get; init; }
}

/// <summary>
/// What changed in a file since the session's latest change to it: it's gone, or lines were added and removed
/// (null when they can't be counted, as in a binary or very large file).
/// </summary>
public sealed record ChangedSince(bool Deleted, int? Added, int? Removed);

/// <summary>
/// The files a tab's session changed, built from its Edit/Write tool calls (DESIGN.md §8). A file's "before" side is
/// the <c>originalFile</c> from Claude's first change to it, so no snapshots are needed. Transcripts leave out a large
/// one, so replays find it in <see cref="Befores"/>.
/// </summary>
/// <remarks>
/// Used from the UI thread only. Tool use ids are remembered, so replaying a transcript that's already been seen
/// doesn't count changes twice.
/// </remarks>
public sealed class ChangedFiles
{
    private readonly TimeProvider _timeProvider;
    private readonly StringComparer _pathComparer;
    private readonly Dictionary<string, (string ToolName, string? Path, JsonObject Input)> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);
    /// <summary>What Claudette's hook read before a call ran, by tool call id: its real path, and its "before" when it could read it.</summary>
    private readonly Dictionary<string, (string Path, string? Before, bool Known)> _hooked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChangedFile> _byPath;
    private readonly List<ChangedFile> _files = [];
    /// <summary>The files changed since the last <see cref="TakeTurn"/>, by normalized path, in order of first change.</summary>
    private readonly Dictionary<string, TurnFileChange> _turnByPath;
    private readonly List<TurnFileChange> _turn = [];

    /// <param name="timeProvider">Stamps each change.</param>
    /// <param name="pathComparer">
    /// How paths are matched. Defaults to <see cref="PlatformPathComparer"/>; tests pass one to check another OS's rule.
    /// </param>
    public ChangedFiles(TimeProvider timeProvider, StringComparer? pathComparer = null)
    {
        _timeProvider = timeProvider;
        _pathComparer = pathComparer ?? PlatformPathComparer;
        _byPath = new Dictionary<string, ChangedFile>(_pathComparer);
        _turnByPath = new Dictionary<string, TurnFileChange>(_pathComparer);
    }

    /// <summary>Case-insensitive on Windows and macOS, case-sensitive on Linux.</summary>
    public static StringComparer PlatformPathComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Files larger than this are listed without line counts or content.</summary>
    public long MaxInspectBytes { get; init; } = TextFiles.DefaultMaxBytes;

    /// <summary>
    /// Where a large file's <c>originalFile</c> is kept for replays, since transcripts leave it out. Null: a replayed
    /// large file's "before" is unknown.
    /// </summary>
    public BeforeContentStore? Befores { get; init; }

    /// <summary>The changed files, in order of first change.</summary>
    public IReadOnlyList<ChangedFile> Files => _files;

    /// <summary>Raised after a change is recorded.</summary>
    public event Action? Changed;

    public static bool IsFileTool(string toolName) => toolName is "Edit" or "Write" or "MultiEdit" or "NotebookEdit";

    /// <summary>Notes a tool call. Only file tools are kept; the change counts once its result succeeds.</summary>
    public void RecordToolUse(string toolUseId, string toolName, JsonObject input)
    {
        if (!IsFileTool(toolName) || _applied.Contains(toolUseId))
        {
            return;
        }
        var path = toolName == "NotebookEdit" ? input.GetString("notebook_path") : input.GetString("file_path");
        _pending[toolUseId] = (toolName, string.IsNullOrEmpty(path) ? null : path, input);
    }

    /// <summary>
    /// What a file tool's call is about to change, as Claudette's PreToolUse hook read it before the call ran (DESIGN.md
    /// §8, "Subagents' changes"): a subagent's result doesn't say. <paramref name="path"/> is the file's real path, which
    /// the call's input may name relative to the subagent's folder. Its <paramref name="before"/> is null when the file
    /// wasn't there, and <paramref name="known"/> false when it couldn't be read. A first change's is kept for replays.
    /// </summary>
    public void NoteBefore(string toolUseId, string path, string? before, bool known)
    {
        if (_applied.Contains(toolUseId))
        {
            return;
        }
        _hooked[toolUseId] = (path, before, known);
        if (known && Find(path) is null)
        {
            if (before is null)
            {
                Befores?.SaveMissing(toolUseId);
            }
            else
            {
                Befores?.Save(toolUseId, before);
            }
        }
    }

    /// <summary>
    /// What a file held before a call, read from the disk for <see cref="NoteBefore"/>: null when it isn't there; not
    /// known when it's binary, too large or can't be read.
    /// </summary>
    public static (string? Before, bool Known) ReadFromDisk(string path, long maxBytes = TextFiles.DefaultMaxBytes)
    {
        var read = TextFiles.Read(path, maxBytes);
        return read.Kind switch
        {
            TextFileKind.Text => (read.Text, true),
            TextFileKind.Missing => (null, true),
            _ => (null, false),
        };
    }

    /// <summary>
    /// Applies a tool call's result. Errors, and results for tool calls that weren't recorded, are ignored.
    /// </summary>
    /// <returns>The file the change was to, or null when nothing was recorded.</returns>
    /// <param name="toolUseResult">The structured <c>tool_use_result</c> (<c>toolUseResult</c> in transcripts).</param>
    /// <param name="at">When the change happened, for transcript replay; defaults to now.</param>
    /// <param name="inTurn">
    /// The change is part of the turn under way, for <see cref="TakeTurn"/>. A transcript read back has no turns to
    /// show, so its changes aren't.
    /// </param>
    public ChangedFile? RecordToolResult(string toolUseId, bool isError, JsonNode? toolUseResult, DateTimeOffset? at = null, bool inTurn = true)
    {
        var hookedFound = _hooked.Remove(toolUseId, out var hooked);
        if (!_pending.Remove(toolUseId, out var pending) || isError)
        {
            return null;
        }
        var result = toolUseResult as JsonObject;
        if (result?.GetString("error") is { Length: > 0 })
        {
            return null; // NotebookEdit reports some failures here.
        }
        if (ResultPath(result) is { } named && pending.Path is { } called && !SamePath(named, called)
            && Path.IsPathFullyQualified(named) && Path.IsPathFullyQualified(called))
        {
            // A message has one structured result, so with several tool results in it, it's another call's: this
            // one's file and "before" aren't known from it.
            result = null;
        }
        // The hook's path is the real one; the call's input may be relative, to a subagent's folder.
        var path = ResultPath(result) ?? (hookedFound ? hooked.Path : pending.Path);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        _applied.Add(toolUseId);
        var when = at ?? _timeProvider.GetUtcNow();
        var key = Normalize(path);
        // This change's own "before": from its result, or what the hook read before it ran.
        var (before, isNew, beforeKnown) = ReadBefore(pending.ToolName, result);
        if (!beforeKnown && hookedFound && hooked.Known)
        {
            (before, isNew, beforeKnown) = (hooked.Before, hooked.Before is null, true);
        }
        _byPath.TryGetValue(key, out var file);
        if (file is null && !beforeKnown && Befores?.Load(toolUseId) is { } saved)
        {
            (before, beforeKnown) = (saved, true);
        }
        else if (file is null && !beforeKnown && Befores?.WasMissing(toolUseId) == true)
        {
            (before, isNew, beforeKnown) = (null, true, true);
        }
        else if (file is null && before is { Length: > BeforeContentStore.TranscriptLimit })
        {
            Befores?.Save(toolUseId, before);
        }
        // What the change was made to: its own "before", or failing that (a transcript leaves a large one out), how the
        // session's previous change left the file.
        var (@base, baseKnown) = beforeKnown ? (before, true) : file is { AfterKnown: true } ? (file.After, true) : (null, false);
        var (after, afterKnown) = FileAfter.Of(pending.ToolName, pending.Input, result, @base, baseKnown);
        // Its "before" isn't how the previous change left the file: something else changed it between them.
        var changedBetween = file is { AfterKnown: true } && beforeKnown && !FileAfter.Same(before ?? "", file.After ?? "");

        if (inTurn)
        {
            if (_turnByPath.TryGetValue(key, out var turnChange))
            {
                var index = _turn.IndexOf(turnChange);
                turnChange = turnChange with { After = after, AfterKnown = afterKnown };
                (_turn[index], _turnByPath[key]) = (turnChange, turnChange);
            }
            else
            {
                // What the file held before this turn's first change to it: this change's own "before".
                var change = new TurnFileChange(path, before, isNew, beforeKnown, after, afterKnown);
                _turnByPath.Add(key, change);
                _turn.Add(change);
            }
        }
        if (file is not null)
        {
            file.AddChange(toolUseId, when, after, afterKnown, changedBetween);
        }
        else
        {
            file = new ChangedFile(path, before, isNew, beforeKnown, toolUseId, when, after, afterKnown);
            _byPath.Add(key, file);
            _files.Add(file);
        }
        Changed?.Invoke();
        return file;
    }

    /// <summary>The changed file at <paramref name="path"/>, or null when Claude hasn't changed it in this session.</summary>
    public ChangedFile? Find(string path) => _byPath.GetValueOrDefault(Normalize(path));

    /// <summary>
    /// The files changed since the last call, in order of their first change, each with what it held before that change
    /// (DESIGN.md §8, "Changes per turn"). Called as each turn ends, which starts the next.
    /// </summary>
    public IReadOnlyList<TurnFileChange> TakeTurn()
    {
        var turn = _turn.ToArray();
        _turn.Clear();
        _turnByPath.Clear();
        return turn;
    }

    /// <summary>
    /// A turn's change to a file: from before it to how its last change left the file, or when that isn't known,
    /// against the file on disk, as the turn has just ended. Null when the lines can't be counted (binary, too large,
    /// or the "before" isn't known).
    /// </summary>
    public (int Added, int Removed)? CountTurn(TurnFileChange change)
    {
        if (!change.BeforeKnown || change.Before is { } before && (LineDiff.LooksBinary(before) || before.Length > MaxInspectBytes))
        {
            return null;
        }
        if (change.AfterKnown)
        {
            return change.After is { } after && (LineDiff.LooksBinary(after) || after.Length > MaxInspectBytes) ? null : LineDiff.Count(change.Before, change.After);
        }
        var read = TextFiles.Read(change.Path, MaxInspectBytes);
        return read.Kind switch
        {
            TextFileKind.Text => LineDiff.Count(change.Before, read.Text),
            TextFileKind.Missing => LineDiff.Count(change.Before, null),
            _ => null,
        };
    }

    /// <summary>Whether two paths name the same file, by this list's rule.</summary>
    public bool SamePath(string a, string b) => _pathComparer.Equals(Normalize(a), Normalize(b));

    /// <summary>
    /// The file's changes in the session: from its "before" to how the session's latest change left it, with what's
    /// changed on disk since (DESIGN.md §8, "This session's changes"). When that "after" isn't known, from its "before"
    /// to the file on disk now.
    /// </summary>
    public ChangedFileState Inspect(ChangedFile file)
    {
        var read = TextFiles.Read(file.Path, MaxInspectBytes);
        return file.AfterKnown ? InspectOwn(file, read) : InspectAgainstDisk(file, read);
    }

    /// <summary>The session's own changes, <see cref="ChangedFile.Before"/> to <see cref="ChangedFile.After"/>, and the disk since.</summary>
    private ChangedFileState InspectOwn(ChangedFile file, TextFileRead read)
    {
        var (before, after) = (file.Before, file.After);
        var binary = before is not null && LineDiff.LooksBinary(before) || after is not null && LineDiff.LooksBinary(after);
        var tooLarge = before?.Length > MaxInspectBytes || after?.Length > MaxInspectBytes;
        var since = read.Kind switch
        {
            TextFileKind.Missing => new ChangedSince(true, null, null),
            TextFileKind.Text when FileAfter.Same(after ?? "", read.Text) => null,
            TextFileKind.Text when !binary && !tooLarge => Counted(LineDiff.Count(after, read.Text)),
            TextFileKind.Text or TextFileKind.Binary => new ChangedSince(false, null, null),
            // Too large to read, or unreadable: nothing to tell it by.
            _ => null,
        };
        var current = read.Kind == TextFileKind.Text ? read.Text : null;
        if (file.IsNew && since is { Deleted: true })
        {
            // Created and then removed again: nothing changed overall.
            return new ChangedFileState(ChangedFileStatus.Unchanged, 0, 0, null);
        }
        if (file.IsNew)
        {
            return new ChangedFileState(ChangedFileStatus.Added, binary || tooLarge ? null : LineDiff.CountLines(after), binary || tooLarge ? null : 0, current)
                { IsBinary = binary, IsTooLarge = tooLarge, Since = since };
        }
        if (!file.BeforeKnown || binary || tooLarge)
        {
            return new ChangedFileState(ChangedFileStatus.Modified, null, null, current) { IsBinary = binary, IsTooLarge = tooLarge, Since = since };
        }
        if (FileAfter.Same(before!, after ?? ""))
        {
            return new ChangedFileState(ChangedFileStatus.Unchanged, 0, 0, current) { Since = since };
        }
        var (own, ownRemoved) = LineDiff.Count(before, after);
        return new ChangedFileState(ChangedFileStatus.Modified, own, ownRemoved, current) { Since = since };
    }

    private static ChangedSince Counted((int Added, int Removed) lines) => new(false, lines.Added, lines.Removed);

    /// <summary>From the file's "before" to the disk now, for a file whose "after" isn't known.</summary>
    private ChangedFileState InspectAgainstDisk(ChangedFile file, TextFileRead read)
    {
        var before = file.Before;
        var beforeBinary = before is not null && LineDiff.LooksBinary(before);
        var beforeTooLarge = before is not null && before.Length > MaxInspectBytes;
        var beforeCountable = file.BeforeKnown && !beforeBinary && !beforeTooLarge;

        switch (read.Kind)
        {
            case TextFileKind.Missing:
                if (file.IsNew)
                {
                    // Created and then removed again: nothing changed overall.
                    return new ChangedFileState(ChangedFileStatus.Unchanged, 0, 0, null);
                }
                return beforeCountable
                    ? new ChangedFileState(ChangedFileStatus.Deleted, 0, LineDiff.CountLines(before), null)
                    : new ChangedFileState(ChangedFileStatus.Deleted, null, null, null) { IsBinary = beforeBinary, IsTooLarge = beforeTooLarge };
            case TextFileKind.TooLarge:
                return new ChangedFileState(file.IsNew ? ChangedFileStatus.Added : ChangedFileStatus.Modified, null, null, null) { IsTooLarge = true };
            case TextFileKind.Binary:
                return new ChangedFileState(file.IsNew ? ChangedFileStatus.Added : ChangedFileStatus.Modified, null, null, null) { IsBinary = true };
            case TextFileKind.Unreadable:
                return new ChangedFileState(file.IsNew ? ChangedFileStatus.Added : ChangedFileStatus.Modified, null, null, null);
        }

        var current = read.Text!;
        if (file.IsNew)
        {
            return new ChangedFileState(ChangedFileStatus.Added, LineDiff.CountLines(current), 0, current);
        }
        if (!beforeCountable)
        {
            return new ChangedFileState(ChangedFileStatus.Modified, null, null, current) { IsBinary = beforeBinary, IsTooLarge = beforeTooLarge };
        }
        if (FileAfter.Same(before!, current))
        {
            return new ChangedFileState(ChangedFileStatus.Unchanged, 0, 0, current);
        }
        var (added, removed) = LineDiff.Count(before, current);
        return new ChangedFileState(ChangedFileStatus.Modified, added, removed, current);
    }

    /// <summary>
    /// The path to show: relative to <paramref name="workingFolder"/> when the file is inside it, otherwise the full
    /// path. Either way with forward slashes.
    /// </summary>
    public string DisplayPath(ChangedFile file, string workingFolder)
    {
        var path = Normalize(file.Path);
        var folder = Normalize(workingFolder);
        if (folder.Length > 0)
        {
            var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            if (path.Length > prefix.Length && _pathComparer.Equals(path[..prefix.Length], prefix))
            {
                return path[prefix.Length..].Replace('\\', '/');
            }
        }
        return path.Replace('\\', '/');
    }

    /// <summary>The file a structured result names: Edit's and Write's <c>filePath</c>, NotebookEdit's <c>notebook_path</c>.</summary>
    private static string? ResultPath(JsonObject? result) =>
        result?.GetString("filePath") is { Length: > 0 } file ? file
        : result?.GetString("notebook_path") is { Length: > 0 } notebook ? notebook
        : null;

    /// <summary>What the file held before the first change, from the tool's structured result.</summary>
    private static (string? Before, bool IsNew, bool BeforeKnown) ReadBefore(string toolName, JsonObject? result)
    {
        if (result is null)
        {
            return (null, false, false);
        }
        if (toolName == "NotebookEdit")
        {
            return result.GetString("original_file") is { } notebook ? (notebook, false, true) : (null, false, false);
        }

        var type = result.GetString("type");
        if (type == "create")
        {
            return (null, true, true);
        }
        if (!result.TryGetPropertyValue("originalFile", out var original))
        {
            return (null, false, false);
        }
        return original switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String => (value.GetValue<string>(), false, true),
            // An Edit with nothing to replace creates the file.
            null when result.GetString("oldString") == "" => (null, true, true),
            // Otherwise it was left out: transcripts leave out one over 10,000 characters, and a Write over a file too
            // large to diff reports null.
            _ => (null, false, false),
        };
    }

    internal static string Normalize(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
