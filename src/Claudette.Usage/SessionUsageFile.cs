using System.Text.Json.Nodes;
using Claudette.Core.Json;
using Claudette.Core.Protocol;

namespace Claudette.Usage;

/// <summary>
/// One turn's use of one model, as machines share it through a synced session (DESIGN.md §6, "Sharing across
/// machines"): counts only, never conversation content.
/// </summary>
/// <param name="Id">The record's own id, the same on every machine, so it's added once however often it's read.</param>
/// <param name="Machine">The id of the machine that ran the turn.</param>
/// <param name="Tab">That machine's id for the tab.</param>
/// <param name="Project">The project's key (<c>Claudette.Core.Git.ProjectKey</c>): the same on every machine with a clone of it.</param>
/// <param name="ProjectName">The project's folder name, to name it on a machine that doesn't have it.</param>
/// <param name="Plan">For the Default account's turns, which plan account it was signed in to (<see cref="UsageSharing.AccountKey"/>).</param>
/// <param name="AccountId">For an API-key account's turns, that machine's id for the account; null for the Default account.</param>
/// <param name="Account">For an API-key account's turns, the account's name, which machines match it by.</param>
public sealed record SharedTurn(
    string Id,
    DateTimeOffset At,
    string Machine,
    string Tab,
    string Model,
    long Input,
    long Output,
    long CacheWrite,
    long CacheRead,
    double CostUsd,
    string? Project = null,
    string? ProjectName = null,
    string? Plan = null,
    string? AccountId = null,
    string? Account = null);

/// <summary>A synced session's usage: every turn any machine ran in it, with the names of those machines and tabs.</summary>
/// <param name="Version">The file's format: <see cref="SessionUsageFile.FileVersion"/> when this Claudette can read it.</param>
/// <param name="Machines">Machine ids and their names.</param>
/// <param name="Tabs">Tab ids and their names.</param>
/// <param name="Turns">Empty when the file's format is newer than this Claudette reads.</param>
public sealed record SessionUsage(int Version, IReadOnlyDictionary<string, string> Machines, IReadOnlyDictionary<string, string> Tabs, IReadOnlyList<SharedTurn> Turns)
{
    public static readonly SessionUsage Empty = new(SessionUsageFile.FileVersion, new Dictionary<string, string>(), new Dictionary<string, string>(), []);

    /// <summary>A later Claudette wrote the file in a format this one can't read, so its turns were left out.</summary>
    public bool IsNewerFormat => Version > SessionUsageFile.FileVersion;
}

/// <summary>
/// The usage file in each synced session's folder in the library (<c>usage.json</c>, DESIGN.md §6, "Sharing across
/// machines"). The machine writing the session merges what's there with what it has, so the file keeps every machine's
/// turns.
/// </summary>
public static class SessionUsageFile
{
    /// <summary>
    /// The file's format. A change that only adds fields keeps it, since readers ignore fields they don't know; one that
    /// removes a field or changes what one means takes the next number.
    /// </summary>
    public const int FileVersion = 1;

    public static string Write(SessionUsage usage)
    {
        var turns = new JsonArray();
        foreach (var turn in usage.Turns.OrderBy(t => t.At).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            var entry = new JsonObject
            {
                ["id"] = turn.Id,
                ["at"] = turn.At.ToUnixTimeMilliseconds(),
                ["machine"] = turn.Machine,
                ["tab"] = turn.Tab,
                ["model"] = turn.Model,
                ["input"] = turn.Input,
                ["output"] = turn.Output,
                ["cacheWrite"] = turn.CacheWrite,
                ["cacheRead"] = turn.CacheRead,
                ["cost"] = turn.CostUsd,
            };
            AddIfSet(entry, "project", turn.Project);
            AddIfSet(entry, "projectName", turn.ProjectName);
            AddIfSet(entry, "plan", turn.Plan);
            AddIfSet(entry, "accountId", turn.AccountId);
            AddIfSet(entry, "account", turn.Account);
            turns.Add(entry);
        }
        return new JsonObject
        {
            ["version"] = Math.Max(usage.Version, FileVersion),
            ["machines"] = Names(usage.Machines),
            ["tabs"] = Names(usage.Tabs),
            ["turns"] = turns,
        }.ToJsonString();
    }

    /// <summary>
    /// Reads a session's usage file. Null for one that isn't one. One in a newer format than <see cref="FileVersion"/>
    /// reads without its turns. A turn without its id, time, machine, tab or model is skipped; a count that's missing reads
    /// as zero, and fields a later version adds are ignored.
    /// </summary>
    public static SessionUsage? Read(string json)
    {
        JsonObject? file;
        try
        {
            file = JsonTree.ParseObject(json);
        }
        catch (Exception)
        {
            return null;
        }
        if (file is null || file.GetDouble("version") is not { } number || file.GetArray("turns") is not { } array)
        {
            return null;
        }
        var version = (int)number;
        var machines = Names(file.GetObject("machines"));
        var tabs = Names(file.GetObject("tabs"));
        if (version > FileVersion)
        {
            return new SessionUsage(version, machines, tabs, []);
        }
        var turns = new List<SharedTurn>();
        foreach (var entry in array.OfType<JsonObject>())
        {
            if (entry.GetString("id") is not { Length: > 0 } id || entry.GetDouble("at") is not { } at || !double.IsFinite(at) || at is < 0 or > MaxMilliseconds
                || entry.GetString("machine") is not { Length: > 0 } machine || entry.GetString("tab") is not { Length: > 0 } tab
                || entry.GetString("model") is not { Length: > 0 } model)
            {
                continue;
            }
            turns.Add(new SharedTurn(
                id,
                DateTimeOffset.FromUnixTimeMilliseconds((long)at),
                machine,
                tab,
                model,
                entry["input"].AsWholeNumber() ?? 0,
                entry["output"].AsWholeNumber() ?? 0,
                entry["cacheWrite"].AsWholeNumber() ?? 0,
                entry["cacheRead"].AsWholeNumber() ?? 0,
                entry.GetDouble("cost") ?? 0,
                entry.GetString("project"),
                entry.GetString("projectName"),
                entry.GetString("plan"),
                entry.GetString("accountId"),
                entry.GetString("account")));
        }
        return new SessionUsage(version, machines, tabs, turns);
    }

    /// <summary>
    /// Both files' turns, each once by its id, and their names, <paramref name="newer"/>'s winning: nothing either one has
    /// is dropped.
    /// </summary>
    public static SessionUsage Merge(SessionUsage older, SessionUsage newer)
    {
        var turns = new Dictionary<string, SharedTurn>(StringComparer.Ordinal);
        foreach (var turn in older.Turns.Concat(newer.Turns))
        {
            turns.TryAdd(turn.Id, turn);
        }
        return new SessionUsage(Math.Max(older.Version, newer.Version), Union(older.Machines, newer.Machines), Union(older.Tabs, newer.Tabs), [.. turns.Values]);

        static Dictionary<string, string> Union(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
        {
            var names = new Dictionary<string, string>(first, StringComparer.Ordinal);
            foreach (var (id, name) in second)
            {
                names[id] = name;
            }
            return names;
        }
    }

    private static JsonObject Names(IReadOnlyDictionary<string, string> names)
    {
        var obj = new JsonObject();
        foreach (var (id, name) in names.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            obj[id] = name;
        }
        return obj;
    }

    private static Dictionary<string, string> Names(JsonObject? obj)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, _) in obj ?? [])
        {
            if (obj!.GetString(id) is { } name)
            {
                names[id] = name;
            }
        }
        return names;
    }

    private static void AddIfSet(JsonObject obj, string name, string? value)
    {
        if (value is not null)
        {
            obj[name] = value;
        }
    }

    /// <summary>9999-12-31, the latest time a <see cref="DateTimeOffset"/> holds.</summary>
    private const double MaxMilliseconds = 253_402_300_799_000;
}
