using System.Globalization;

namespace Claudette.Core.Perforce;

/// <summary>Where a file stands in Perforce, from <c>p4 fstat</c> (DESIGN.md §18, "Perforce in the Files tab").</summary>
public enum PerforceFileStatus
{
    /// <summary>Perforce doesn't have it, and it isn't opened: a new file, or one Perforce ignores.</summary>
    NotInDepot,

    /// <summary>In the depot, and not opened here.</summary>
    InDepot,

    /// <summary>Opened for edit here (<c>p4 edit</c>).</summary>
    CheckedOut,

    /// <summary>Opened for add here (<c>p4 add</c>).</summary>
    MarkedForAdd,

    /// <summary>Opened for delete here (<c>p4 delete</c>).</summary>
    MarkedForDelete,

    /// <summary>The new name of a file opened for move here (<c>move/add</c>).</summary>
    MovedHere,

    /// <summary>The old name of a file opened for move here (<c>move/delete</c>).</summary>
    MovedAway,

    /// <summary>Opened here some other way: for integrate, branch, import and the like.</summary>
    Opened,
}

/// <summary>
/// One file's state in Perforce, from one record of <c>p4 -ztag fstat</c> (DESIGN.md §18, "Perforce in the Files tab").
/// </summary>
/// <param name="Path">The file's local path (fstat's <c>clientFile</c>), with Perforce's escapes undone.</param>
/// <param name="Changelist">The changelist it's opened in here: a number, or <c>default</c>; null when it isn't opened.</param>
/// <param name="Action">What it's opened for here, in Perforce's words (<c>edit</c>, <c>integrate</c>…); null when it isn't.</param>
/// <param name="HaveRevision">The revision synced here (<c>#have</c>); null when none is.</param>
/// <param name="MovedFile">For a moved file, the depot path of its other half.</param>
/// <param name="OtherOpens">Who else has it open (<c>user@workspace</c>), from fstat's <c>otherOpen</c> fields.</param>
public sealed record PerforceFileState(
    string Path,
    PerforceFileStatus Status,
    string? DepotFile = null,
    string? Changelist = null,
    string? Action = null,
    int? HaveRevision = null,
    string? MovedFile = null,
    IReadOnlyList<string>? OtherOpens = null)
{
    /// <summary>The fields <c>p4 fstat -T</c> asks for: no more than the marks and the menus need.</summary>
    public const string Fields = "clientFile,depotFile,headAction,haveRev,action,change,movedFile,otherOpen";

    public IReadOnlyList<string> OthersOpening => OtherOpens ?? [];

    /// <summary>It's opened here, for anything.</summary>
    public bool IsOpen => Status is not (PerforceFileStatus.NotInDepot or PerforceFileStatus.InDepot);

    /// <summary>Perforce has a revision of it to compare with: <b>Diff against the depot</b> applies.</summary>
    public bool HasDepotRevision => HaveRevision is not null && Status is not (PerforceFileStatus.MarkedForAdd or PerforceFileStatus.MovedHere or PerforceFileStatus.NotInDepot);

    public bool IsOpenedByOthers => OthersOpening.Count > 0;

    /// <summary>A file Perforce doesn't have, which this folder's listing didn't name.</summary>
    public static PerforceFileState NotInDepotAt(string path) => new(path, PerforceFileStatus.NotInDepot);

    /// <summary>
    /// Reads <c>p4 -ztag fstat</c>'s records. Tolerant: unknown fields are ignored, a record without a local path is
    /// skipped, and an action Claudette doesn't know counts as <see cref="PerforceFileStatus.Opened"/>.
    /// </summary>
    public static IReadOnlyList<PerforceFileState> ParseFstat(string ztag)
    {
        var states = new List<PerforceFileState>();
        foreach (var record in ZTag.Parse(ztag))
        {
            if (record.GetValueOrDefault("clientFile") is not { Length: > 0 } clientFile)
            {
                continue;
            }
            var action = record.GetValueOrDefault("action") is { Length: > 0 } a ? a : null;
            var headAction = record.GetValueOrDefault("headAction");
            int? have = int.TryParse(record.GetValueOrDefault("haveRev"), NumberStyles.None, CultureInfo.InvariantCulture, out var rev) && rev > 0 ? rev : null;
            var status = action switch
            {
                "edit" => PerforceFileStatus.CheckedOut,
                "add" => PerforceFileStatus.MarkedForAdd,
                "delete" => PerforceFileStatus.MarkedForDelete,
                "move/add" => PerforceFileStatus.MovedHere,
                "move/delete" => PerforceFileStatus.MovedAway,
                { } => PerforceFileStatus.Opened,
                // Deleted at the head revision and not synced: Perforce no longer has it to edit.
                null when have is null && headAction is "delete" or "move/delete" => PerforceFileStatus.NotInDepot,
                null when headAction is not null || have is not null => PerforceFileStatus.InDepot,
                null => PerforceFileStatus.NotInDepot,
            };
            var others = record.Where(f => f.Key.StartsWith("otherOpen", StringComparison.Ordinal) && f.Key.Length > "otherOpen".Length && char.IsAsciiDigit(f.Key["otherOpen".Length]))
                .OrderBy(f => int.TryParse(f.Key["otherOpen".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
                .Select(f => f.Value)
                .Where(v => v.Length > 0)
                .ToArray();
            states.Add(new PerforceFileState(
                PerforceFileCommands.Unescape(clientFile),
                status,
                record.GetValueOrDefault("depotFile"),
                action is null ? null : record.GetValueOrDefault("change") is { Length: > 0 } change ? change : "default",
                action,
                have,
                record.GetValueOrDefault("movedFile") is { Length: > 0 } moved ? moved : null,
                others));
        }
        return states;
    }

    /// <summary>"CL 12345", or "the default changelist".</summary>
    public string ChangelistText => Changelist is null or "default" ? "the default changelist" : $"CL {Changelist}";

    /// <summary>What a row's mark says, as its tip and to a screen reader: <i>Checked out in CL 12345</i>. Null for no mark.</summary>
    public string? Describe()
    {
        var own = Status switch
        {
            PerforceFileStatus.CheckedOut => $"Checked out in {ChangelistText}",
            PerforceFileStatus.MarkedForAdd => $"Marked for add in {ChangelistText}",
            PerforceFileStatus.MarkedForDelete => $"Marked for delete in {ChangelistText}",
            PerforceFileStatus.MovedHere => MovedFile is { } from ? $"Moved from {DepotName(from)} in {ChangelistText}" : $"Moved here in {ChangelistText}",
            PerforceFileStatus.MovedAway => MovedFile is { } to ? $"Moved to {DepotName(to)} in {ChangelistText}" : $"Moved away in {ChangelistText}",
            PerforceFileStatus.Opened => $"Opened for {Action} in {ChangelistText}",
            _ => null,
        };
        var others = OthersOpening.Count switch
        {
            0 => null,
            1 => $"opened by {OthersOpening[0]}",
            2 => $"opened by {OthersOpening[0]} and {OthersOpening[1]}",
            _ => $"opened by {OthersOpening[0]} and {OthersOpening.Count - 1} others",
        };
        return (own, others) switch
        {
            (null, null) => null,
            ({ } mine, null) => $"{mine}.",
            (null, { } theirs) => $"{char.ToUpperInvariant(theirs[0])}{theirs[1..]}.",
            ({ } mine, { } theirs) => $"{mine}. Also {theirs}.",
        };
    }

    /// <summary>A depot path's file name, with the folder it's in: <c>Player/Old.cpp</c>.</summary>
    private static string DepotName(string depotPath)
    {
        var parts = depotPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : depotPath;
    }
}
