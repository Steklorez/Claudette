using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Json;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Usage;

/// <summary>
/// The local usage history: a SQLite file in the app data folder (DESIGN.md §6, "Usage history").
/// <list type="bullet">
/// <item>Plan usage samples, app-wide. One is saved only when a value changed, and at most once a minute.</item>
/// <item>Samples other machines shared through the session library, imported with the name of the machine they came
/// from (DESIGN.md §6, "Sharing across machines").</item>
/// <item>Per-turn token records, per tab, and each tab's last known name.</item>
/// <item>Turns other machines ran in synced sessions, with their machines' names (<see cref="SessionUsageFile"/>).</item>
/// <item>Each turn's model calls, its tools' results, and what changed what later calls cost (compactions, clears): counts
/// and names only (DESIGN.md §18, "What's recorded").</item>
/// </list>
/// No conversation content is ever stored. Synchronous and safe to call from any thread; keep it off the UI thread.
/// Times are stored to the millisecond.
/// </summary>
public sealed class UsageStore : IDisposable
{
    public const int SchemaVersion = UsageSchema.Version;

    /// <summary>The <c>imports</c> row that holds back every machine's samples up to a time: the last Clear.</summary>
    private const string EveryMachine = "*";
    public static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromMinutes(1);

    private const string SelectSamples =
        "SELECT timestamp, session_percent, session_resets_at, weekly_percent, weekly_resets_at, models FROM samples";

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private SqliteConnection _connection;
    private SampleValues? _lastSaved;
    private DateTimeOffset? _lastWrite;
    private bool _disposed;

    /// <summary>How far ahead of this machine's clock another machine's sample may be and still be imported.</summary>
    public static readonly TimeSpan FutureAllowance = TimeSpan.FromMinutes(5);

    /// <summary>Opens the file, creating it, its folder and the schema as needed.</summary>
    /// <remarks>A file that isn't a readable database is kept aside as <c>*.bad</c> and a new one is started.</remarks>
    public UsageStore(string databasePath, TimeProvider time, ILogger? logger = null)
    {
        Path = System.IO.Path.GetFullPath(databasePath);
        _time = time;
        _logger = logger ?? NullLogger.Instance;
        if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }
        _connection = OpenOrReplace();

        if (ReadLatestSample(ownOnly: true) is { } latest)
        {
            _lastSaved = SampleValues.From(latest);
            _lastWrite = latest.Timestamp;
        }
    }

    public string Path { get; }

    /// <summary>
    /// Saves the snapshot's session, weekly and model-specific readings if any of them changed since the last saved
    /// sample and a minute has passed since then. A change inside the minute is held: the next call after the minute
    /// writes it, if the values still differ. Returns whether a sample was written.
    /// </summary>
    public bool AddSample(UsageSnapshot snapshot)
    {
        if (snapshot.Limits.Count == 0)
        {
            return false;
        }
        var values = SampleValues.From(snapshot);
        return Locked(() =>
        {
            if (values == _lastSaved)
            {
                return false;
            }
            var now = _time.GetUtcNow();
            // A last write in the future means the clock was set back since: don't wait for it to catch up.
            if (_lastWrite is { } last && now >= last && now - last < MinimumSampleInterval)
            {
                return false;
            }

            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO samples (timestamp, session_percent, session_resets_at, weekly_percent, weekly_resets_at, models)
                VALUES ($timestamp, $sessionPercent, $sessionResetsAt, $weeklyPercent, $weeklyResetsAt, $models)
                """;
            Add(command, "$timestamp", now.ToUnixTimeMilliseconds());
            Add(command, "$sessionPercent", values.SessionPercent);
            Add(command, "$sessionResetsAt", values.SessionResetsAt);
            Add(command, "$weeklyPercent", values.WeeklyPercent);
            Add(command, "$weeklyResetsAt", values.WeeklyResetsAt);
            Add(command, "$models", values.Models);
            command.ExecuteNonQuery();
            _lastSaved = values;
            _lastWrite = now;
            return true;
        });
    }

    /// <summary>The newest sample, from this machine or another, so the header can show something after a restart.</summary>
    public UsageSample? GetLatestSample() => Locked(() => ReadLatestSample(ownOnly: false));

    /// <summary>This machine's own samples since <paramref name="from"/>, oldest first: what it shares, never another's.</summary>
    public IReadOnlyList<UsageSample> GetOwnSamples(DateTimeOffset from) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"{SelectSamples} WHERE machine IS NULL AND timestamp >= $from ORDER BY timestamp, id";
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        return ReadSamples(command);
    });

    /// <summary>
    /// Adds the samples another machine shared that are newer than any imported from it before, and newer than the last
    /// Clear, so each is imported once and cleared history doesn't come back. Returns how many were added.
    /// </summary>
    public int ImportSamples(string machine, IEnumerable<UsageSample> samples)
    {
        ArgumentException.ThrowIfNullOrEmpty(machine);
        return Locked(() =>
        {
            using var transaction = _connection.BeginTransaction();
            using var watermark = _connection.CreateCommand();
            watermark.Transaction = transaction;
            watermark.CommandText = "SELECT MAX(through) FROM imports WHERE machine IN ($machine, $every)";
            Add(watermark, "$machine", machine);
            Add(watermark, "$every", EveryMachine);
            var through = watermark.ExecuteScalar() is long stored ? stored : long.MinValue;

            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO samples (timestamp, session_percent, session_resets_at, weekly_percent, weekly_resets_at, models, machine)
                VALUES ($timestamp, $sessionPercent, $sessionResetsAt, $weeklyPercent, $weeklyResetsAt, $models, $machine)
                """;
            var added = 0;
            var newest = through;
            // A machine whose clock is ahead shares samples from the future. Imported, they'd move its mark ahead and
            // stand as the newest reading; left for now, they come in once their time has come.
            var latest = (_time.GetUtcNow() + FutureAllowance).ToUnixTimeMilliseconds();
            foreach (var sample in samples)
            {
                var timestamp = sample.Timestamp.ToUnixTimeMilliseconds();
                if (timestamp <= through || timestamp > latest)
                {
                    continue;
                }
                var values = SampleValues.From(sample);
                insert.Parameters.Clear();
                Add(insert, "$timestamp", timestamp);
                Add(insert, "$sessionPercent", values.SessionPercent);
                Add(insert, "$sessionResetsAt", values.SessionResetsAt);
                Add(insert, "$weeklyPercent", values.WeeklyPercent);
                Add(insert, "$weeklyResetsAt", values.WeeklyResetsAt);
                Add(insert, "$models", values.Models);
                Add(insert, "$machine", machine);
                insert.ExecuteNonQuery();
                added++;
                newest = Math.Max(newest, timestamp);
            }
            if (newest > through)
            {
                using var mark = _connection.CreateCommand();
                mark.Transaction = transaction;
                mark.CommandText = "INSERT INTO imports (machine, through) VALUES ($machine, $through) ON CONFLICT (machine) DO UPDATE SET through = excluded.through";
                Add(mark, "$machine", machine);
                Add(mark, "$through", newest);
                mark.ExecuteNonQuery();
            }
            transaction.Commit();
            return added;
        });
    }

    /// <summary>Samples from <paramref name="from"/> to <paramref name="to"/> (both inclusive), oldest first.</summary>
    public IReadOnlyList<UsageSample> GetSamples(DateTimeOffset from, DateTimeOffset to) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"{SelectSamples} WHERE timestamp >= $from AND timestamp <= $to ORDER BY timestamp, id";
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        Add(command, "$to", to.ToUnixTimeMilliseconds());
        return ReadSamples(command);
    });

    /// <summary>
    /// The highest usage each window reached, for every window in the history that reset by <paramref name="now"/>, newest
    /// first (the Usage panel's past sessions and weeks, DESIGN.md §6). Reset times are compared to the second, since the
    /// two usage sources differ by fractions of one, so they are rounded to it.
    /// </summary>
    public IReadOnlyList<WindowPeak> GetPastWindows(UsageWindow window, DateTimeOffset now)
    {
        var (percent, resetsAt) = window == UsageWindow.Session ? ("session_percent", "session_resets_at") : ("weekly_percent", "weekly_resets_at");
        return Locked(() =>
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT ({resetsAt} + 500) / 1000, MAX({percent}) FROM samples
                WHERE {resetsAt} IS NOT NULL AND {percent} IS NOT NULL AND {resetsAt} <= $now
                GROUP BY ({resetsAt} + 500) / 1000
                ORDER BY 1 DESC
                """;
            Add(command, "$now", now.ToUnixTimeMilliseconds());
            using var reader = command.ExecuteReader();
            var peaks = new List<WindowPeak>();
            while (reader.Read())
            {
                peaks.Add(new WindowPeak(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), reader.GetDouble(1)));
            }
            return peaks;
        });
    }

    /// <summary>
    /// Records this machine's turns, each with an id of its own that it keeps on every machine it's shared with (DESIGN.md
    /// §6, "Sharing across machines"), and with them, in the same transaction, their calls, steps and events (DESIGN.md
    /// §18, "What's recorded").
    /// </summary>
    public void AddTurns(IEnumerable<TurnRecord> turns, TurnDetail? detail = null) => Locked(() =>
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO turns (timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd, project, account,
                               uid, project_key, project_name, account_name, plan, turn, idle_ms)
            VALUES ($timestamp, $tabId, $sessionId, $model, $input, $output, $cacheWrite, $cacheRead, $costUsd, $project, $account,
                    $uid, $projectKey, $projectName, $accountName, $plan, $turn, $idleMs)
            """;
        foreach (var turn in turns)
        {
            command.Parameters.Clear();
            Add(command, "$timestamp", turn.Timestamp.ToUnixTimeMilliseconds());
            Add(command, "$tabId", turn.TabId);
            Add(command, "$sessionId", turn.SessionId);
            Add(command, "$model", turn.Model);
            Add(command, "$input", turn.Input);
            Add(command, "$output", turn.Output);
            Add(command, "$cacheWrite", turn.CacheWrite);
            Add(command, "$cacheRead", turn.CacheRead);
            Add(command, "$costUsd", turn.CostUsd);
            Add(command, "$project", turn.Project);
            Add(command, "$account", turn.Account);
            Add(command, "$uid", Guid.NewGuid().ToString("N"));
            Add(command, "$projectKey", turn.ProjectKey);
            Add(command, "$projectName", turn.ProjectName);
            Add(command, "$accountName", turn.AccountName);
            Add(command, "$plan", turn.Plan);
            Add(command, "$turn", turn.Turn);
            Add(command, "$idleMs", turn.IdleBefore is { } idle ? (long)idle.TotalMilliseconds : null);
            command.ExecuteNonQuery();
        }
        if (detail is not null)
        {
            InsertDetail(transaction, detail);
        }
        transaction.Commit();
    });

    /// <summary>
    /// Records something that changed what a tab's later calls cost, outside a turn: the conversation cleared (DESIGN.md
    /// §18, "What's recorded").
    /// </summary>
    public void AddEvent(UsageEvent usageEvent) => Locked(() =>
    {
        using var transaction = _connection.BeginTransaction();
        InsertDetail(transaction, new TurnDetail([], [], [usageEvent]));
        transaction.Commit();
    });

    private void InsertDetail(SqliteTransaction transaction, TurnDetail detail)
    {
        using var calls = _connection.CreateCommand();
        calls.Transaction = transaction;
        calls.CommandText = """
            INSERT INTO calls (turn, tab_id, session_id, timestamp, seq, message_id, agent, agent_type, model, input, output, cache_read,
                               write_5m, write_1h, web_searches, web_fetches, kind, cost_usd, source, machine)
            VALUES ($turn, $tabId, $sessionId, $timestamp, $seq, $messageId, $agent, $agentType, $model, $input, $output, $cacheRead,
                    $write5m, $write1h, $webSearches, $webFetches, $kind, $costUsd, $source, $machine)
            """;
        foreach (var call in detail.Calls)
        {
            calls.Parameters.Clear();
            Add(calls, "$turn", call.Turn);
            Add(calls, "$tabId", call.TabId);
            Add(calls, "$sessionId", call.SessionId);
            Add(calls, "$timestamp", call.Timestamp.ToUnixTimeMilliseconds());
            Add(calls, "$seq", call.Seq);
            Add(calls, "$messageId", call.MessageId);
            Add(calls, "$agent", call.Agent);
            Add(calls, "$agentType", call.AgentType);
            Add(calls, "$model", call.Model);
            Add(calls, "$input", call.Input);
            Add(calls, "$output", call.Output);
            Add(calls, "$cacheRead", call.CacheRead);
            Add(calls, "$write5m", call.Write5m);
            Add(calls, "$write1h", call.Write1h);
            Add(calls, "$webSearches", call.WebSearches);
            Add(calls, "$webFetches", call.WebFetches);
            Add(calls, "$kind", (int)call.Kind);
            Add(calls, "$costUsd", call.CostUsd);
            Add(calls, "$source", (int)call.Source);
            Add(calls, "$machine", call.Machine);
            calls.ExecuteNonQuery();
        }
        using var steps = _connection.CreateCommand();
        steps.Transaction = transaction;
        steps.CommandText = """
            INSERT INTO steps (turn, call_seq, agent, tool, tool_use, tokens, is_error, timestamp, machine)
            VALUES ($turn, $callSeq, $agent, $tool, $toolUse, $tokens, $isError, $timestamp, $machine)
            """;
        foreach (var step in detail.Steps)
        {
            steps.Parameters.Clear();
            Add(steps, "$turn", step.Turn);
            Add(steps, "$callSeq", step.CallSeq);
            Add(steps, "$agent", step.Agent);
            Add(steps, "$tool", step.Tool);
            Add(steps, "$toolUse", step.ToolUseId);
            Add(steps, "$tokens", step.Tokens);
            Add(steps, "$isError", step.IsError ? 1 : 0);
            Add(steps, "$timestamp", step.Timestamp.ToUnixTimeMilliseconds());
            Add(steps, "$machine", step.Machine);
            steps.ExecuteNonQuery();
        }
        using var events = _connection.CreateCommand();
        events.Transaction = transaction;
        events.CommandText = """
            INSERT INTO events (tab_id, session_id, turn, timestamp, kind, value, detail, machine)
            VALUES ($tabId, $sessionId, $turn, $timestamp, $kind, $value, $detail, $machine)
            """;
        foreach (var usageEvent in detail.Events)
        {
            events.Parameters.Clear();
            Add(events, "$tabId", usageEvent.TabId);
            Add(events, "$sessionId", usageEvent.SessionId);
            Add(events, "$turn", usageEvent.Turn);
            Add(events, "$timestamp", usageEvent.Timestamp.ToUnixTimeMilliseconds());
            Add(events, "$kind", (int)usageEvent.Kind);
            Add(events, "$value", usageEvent.Value);
            Add(events, "$detail", usageEvent.Detail);
            Add(events, "$machine", usageEvent.Machine);
            events.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The model calls recorded between <paramref name="from"/> and <paramref name="to"/>, only <paramref name="tabId"/>'s
    /// when it's given, oldest first and each turn's in order (DESIGN.md §18, "What's recorded").
    /// </summary>
    public IReadOnlyList<CallRecord> GetCalls(DateTimeOffset from, DateTimeOffset to, string? tabId = null) => Locked(() =>
        ReadCalls($"WHERE timestamp >= $from AND timestamp <= $to {(tabId is null ? "" : "AND tab_id = $tabId")}", command =>
        {
            Add(command, "$from", from.ToUnixTimeMilliseconds());
            Add(command, "$to", to.ToUnixTimeMilliseconds());
            if (tabId is not null)
            {
                Add(command, "$tabId", tabId);
            }
        }));

    private List<CallRecord> ReadCalls(string where, Action<SqliteCommand> parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT turn, tab_id, session_id, timestamp, seq, message_id, agent, agent_type, model, input, output, cache_read, write_5m, write_1h,
                   web_searches, web_fetches, kind, cost_usd, source, machine
            FROM calls {where}
            ORDER BY timestamp, turn, seq
            """;
        parameters(command);
        var calls = new List<CallRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            calls.Add(new CallRecord(
                reader.GetString(0),
                reader.GetString(1),
                NullableString(reader, 2),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.GetInt32(4),
                NullableString(reader, 5),
                NullableString(reader, 6),
                NullableString(reader, 7),
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.GetInt64(12),
                reader.GetInt64(13),
                reader.GetInt32(14),
                reader.GetInt32(15),
                (CallKind)reader.GetInt32(16),
                reader.GetDouble(17),
                (CallSource)reader.GetInt32(18))
            {
                Machine = NullableString(reader, 19),
            });
        }
        return calls;
    }

    /// <summary>The tool results of the given turns, each turn's in order of the calls that asked for them.</summary>
    public IReadOnlyList<StepRecord> GetSteps(IEnumerable<string> turns) => Locked(() => ReadSteps(turns));

    private List<StepRecord> ReadSteps(IEnumerable<string> turns)
    {
        var steps = new List<StepRecord>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT turn, call_seq, agent, tool, tokens, is_error, timestamp, machine, tool_use FROM steps WHERE turn = $turn ORDER BY call_seq, id";
        foreach (var turn in turns.Distinct(StringComparer.Ordinal))
        {
            command.Parameters.Clear();
            Add(command, "$turn", turn);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                steps.Add(new StepRecord(reader.GetString(0), reader.GetInt32(1), NullableString(reader, 2), reader.GetString(3), reader.GetInt64(4),
                    reader.GetInt64(5) != 0, DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)))
                {
                    Machine = NullableString(reader, 7),
                    ToolUseId = NullableString(reader, 8),
                });
            }
        }
        return steps;
    }

    /// <summary>The events recorded between <paramref name="from"/> and <paramref name="to"/>, only <paramref name="tabId"/>'s when it's given, oldest first.</summary>
    public IReadOnlyList<UsageEvent> GetEvents(DateTimeOffset from, DateTimeOffset to, string? tabId = null) => Locked(() =>
        ReadEvents($"WHERE timestamp >= $from AND timestamp <= $to {(tabId is null ? "" : "AND tab_id = $tabId")}", command =>
        {
            Add(command, "$from", from.ToUnixTimeMilliseconds());
            Add(command, "$to", to.ToUnixTimeMilliseconds());
            if (tabId is not null)
            {
                Add(command, "$tabId", tabId);
            }
        }));

    private List<UsageEvent> ReadEvents(string where, Action<SqliteCommand> parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT tab_id, session_id, turn, timestamp, kind, value, detail, machine
            FROM events {where}
            ORDER BY timestamp, id
            """;
        parameters(command);
        var events = new List<UsageEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            events.Add(new UsageEvent(reader.GetString(0), NullableString(reader, 1), NullableString(reader, 2), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                (UsageEventKind)reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt64(5), NullableString(reader, 6))
            {
                Machine = NullableString(reader, 7),
            });
        }
        return events;
    }

    /// <summary>
    /// A synced session's turns as this machine has them, to share in the session's usage file (DESIGN.md §6, "Sharing
    /// across machines"): its own, under <paramref name="thisMachine"/>, and those other machines shared. Turns recorded
    /// before turns were shared have no id and are left out.
    /// </summary>
    /// <param name="thisMachineName">This machine's name, for the file's list of machines.</param>
    public SessionUsage GetSessionUsage(string sessionId, string thisMachine, string thisMachineName) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT uid, timestamp, COALESCE(machine, $thisMachine), tab_id, model, input, output, cache_write, cache_read, cost_usd,
                   project_key, project_name, plan, account, account_name, turn
            FROM turns WHERE session_id = $sessionId AND uid IS NOT NULL
            ORDER BY timestamp, id
            """;
        Add(command, "$sessionId", sessionId);
        Add(command, "$thisMachine", thisMachine);
        var turns = new List<SharedTurn>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                turns.Add(new SharedTurn(
                    reader.GetString(0),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetDouble(9),
                    NullableString(reader, 10),
                    NullableString(reader, 11),
                    NullableString(reader, 12),
                    NullableString(reader, 13),
                    NullableString(reader, 14))
                {
                    Turn = NullableString(reader, 15),
                });
            }
        }
        var machines = ReadNames("SELECT id, name FROM machines", turns.Select(t => t.Machine));
        machines[thisMachine] = thisMachineName;
        var tabs = ReadNames("SELECT tab_id, name FROM tabs", turns.Select(t => t.Tab));
        var shared = turns.Select(t => t.Turn).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new SessionUsage(SessionUsageFile.FileVersion, machines, tabs, turns)
        {
            Calls = [.. ReadCalls("WHERE session_id = $sessionId", command => Add(command, "$sessionId", sessionId))
                .Where(c => shared.Contains(c.Turn)).Select(c => c with { SessionId = null, Machine = c.Machine ?? thisMachine })],
            Steps = [.. ReadSteps(shared).Select(s => s with { Machine = s.Machine ?? thisMachine })],
            Events = [.. ReadEvents("WHERE session_id = $sessionId", command => Add(command, "$sessionId", sessionId))
                .Select(e => e with { SessionId = null, Machine = e.Machine ?? thisMachine })],
        };
    });

    /// <summary>
    /// Adds the calls, steps and events another machine shared in a synced session (DESIGN.md §18, "What's recorded"), for
    /// <paramref name="turns"/> only, the turns of theirs this machine counts: each call once by its turn and place, a
    /// turn's steps once, and each event once. None older than <paramref name="keepFor"/> or the last Clear are added.
    /// Returns how many calls were added.
    /// </summary>
    public int ImportDetail(string sessionId, SessionUsage usage, IReadOnlySet<string> turns, TimeSpan? keepFor) => Locked(() =>
    {
        var now = _time.GetUtcNow();
        using var transaction = _connection.BeginTransaction();
        using var floor = _connection.CreateCommand();
        floor.Transaction = transaction;
        floor.CommandText = "SELECT through FROM imports WHERE machine = $every";
        Add(floor, "$every", EveryMachine);
        var cutoff = Math.Max(floor.ExecuteScalar() is long cleared ? cleared : long.MinValue, keepFor is { } keep ? (now - keep).ToUnixTimeMilliseconds() : long.MinValue);
        bool Keeps(DateTimeOffset at) => at.ToUnixTimeMilliseconds() >= cutoff;

        using var known = _connection.CreateCommand();
        known.Transaction = transaction;
        known.CommandText = "SELECT 1 FROM calls WHERE turn = $turn AND seq = $seq";
        var calls = usage.Calls.Where(c => turns.Contains(c.Turn) && c.Machine is not null && Keeps(c.Timestamp)).Where(c =>
        {
            known.Parameters.Clear();
            Add(known, "$turn", c.Turn);
            Add(known, "$seq", c.Seq);
            return known.ExecuteScalar() is null;
        }).Select(c => c with { SessionId = sessionId }).ToList();
        known.CommandText = "SELECT 1 FROM steps WHERE turn = $turn";
        var steps = usage.Steps.Where(s => turns.Contains(s.Turn) && s.Machine is not null && Keeps(s.Timestamp)).GroupBy(s => s.Turn, StringComparer.Ordinal).Where(g =>
        {
            known.Parameters.Clear();
            Add(known, "$turn", g.Key);
            return known.ExecuteScalar() is null;
        }).SelectMany(g => g).ToList();
        known.CommandText = "SELECT 1 FROM events WHERE tab_id = $tab AND timestamp = $at AND kind = $kind";
        var events = usage.Events.Where(e => e.Machine is not null && (e.Turn is null || turns.Contains(e.Turn)) && Keeps(e.Timestamp)).Where(e =>
        {
            known.Parameters.Clear();
            Add(known, "$tab", e.TabId);
            Add(known, "$at", e.Timestamp.ToUnixTimeMilliseconds());
            Add(known, "$kind", (int)e.Kind);
            return known.ExecuteScalar() is null;
        }).Select(e => e with { SessionId = sessionId }).ToList();
        InsertDetail(transaction, new TurnDetail(calls, steps, events));
        transaction.Commit();
        return calls.Count;
    });

    /// <summary>
    /// Adds turns another machine shared in a synced session (DESIGN.md §6, "Sharing across machines"), each once by its
    /// id, with the names of their machines and tabs. Turns older than <paramref name="keepFor"/> or the last Clear aren't
    /// added. One from a machine whose clock is ahead keeps its time, which only places it in a period, so two of its turns
    /// stay two. Returns the turns added.
    /// </summary>
    /// <param name="sessionId">The session the turns were run in.</param>
    public IReadOnlyList<SharedTurn> ImportTurns(string sessionId, IEnumerable<SharedTurn> turns, IReadOnlyDictionary<string, string> machines,
        IReadOnlyDictionary<string, string> tabs, TimeSpan? keepFor) => Locked(() =>
    {
        var now = _time.GetUtcNow();
        using var transaction = _connection.BeginTransaction();
        using var floor = _connection.CreateCommand();
        floor.Transaction = transaction;
        floor.CommandText = "SELECT through FROM imports WHERE machine = $every";
        Add(floor, "$every", EveryMachine);
        var cutoff = Math.Max(floor.ExecuteScalar() is long cleared ? cleared : long.MinValue, keepFor is { } keep ? (now - keep).ToUnixTimeMilliseconds() : long.MinValue);

        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO turns (timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd, account,
                                         uid, machine, project_key, project_name, account_name, plan, turn)
            VALUES ($timestamp, $tabId, $sessionId, $model, $input, $output, $cacheWrite, $cacheRead, $costUsd, $account,
                    $uid, $machine, $projectKey, $projectName, $accountName, $plan, $turn)
            """;
        var added = new List<SharedTurn>();
        foreach (var turn in turns)
        {
            var timestamp = turn.At.ToUnixTimeMilliseconds();
            if (timestamp < cutoff)
            {
                continue;
            }
            insert.Parameters.Clear();
            Add(insert, "$timestamp", timestamp);
            Add(insert, "$tabId", turn.Tab);
            Add(insert, "$sessionId", sessionId);
            Add(insert, "$model", turn.Model);
            Add(insert, "$input", turn.Input);
            Add(insert, "$output", turn.Output);
            Add(insert, "$cacheWrite", turn.CacheWrite);
            Add(insert, "$cacheRead", turn.CacheRead);
            Add(insert, "$costUsd", turn.CostUsd);
            Add(insert, "$account", turn.AccountId);
            Add(insert, "$uid", turn.Id);
            Add(insert, "$machine", turn.Machine);
            Add(insert, "$projectKey", turn.Project);
            Add(insert, "$projectName", turn.ProjectName);
            // Matched to this machine's accounts without the spaces at its ends, as names are compared.
            Add(insert, "$accountName", turn.Account?.Trim());
            Add(insert, "$plan", turn.Plan);
            Add(insert, "$turn", turn.Turn);
            if (insert.ExecuteNonQuery() > 0)
            {
                added.Add(turn);
            }
        }
        using var names = _connection.CreateCommand();
        names.Transaction = transaction;
        foreach (var machine in added.Select(t => t.Machine).Distinct(StringComparer.Ordinal))
        {
            if (machines.TryGetValue(machine, out var name))
            {
                names.Parameters.Clear();
                names.CommandText = "INSERT INTO machines (id, name) VALUES ($id, $name) ON CONFLICT (id) DO UPDATE SET name = excluded.name";
                Add(names, "$id", machine);
                Add(names, "$name", name);
                names.ExecuteNonQuery();
            }
        }
        foreach (var (tab, name) in tabs)
        {
            names.Parameters.Clear();
            // Only for another machine's tab with turns here: never this machine's own tabs' names.
            names.CommandText = """
                INSERT INTO tabs (tab_id, name) SELECT $tabId, $name WHERE EXISTS (SELECT 1 FROM turns WHERE tab_id = $tabId AND machine IS NOT NULL)
                ON CONFLICT (tab_id) DO UPDATE SET name = excluded.name
                """;
            Add(names, "$tabId", tab);
            Add(names, "$name", name);
            names.ExecuteNonQuery();
        }
        transaction.Commit();
        return added;
    });

    /// <summary>The names of the machines that shared turns, by their ids.</summary>
    public IReadOnlyDictionary<string, string> GetMachineNames() => Locked(() => ReadNames("SELECT id, name FROM machines", null));

    /// <summary>
    /// Gives this machine's turns recorded before projects had keys the key of their folder, where <paramref name="keyFor"/>
    /// knows it (the folder still exists), so a project's older and newer turns are one row in the Usage panel. Their counts
    /// are left as they are. Returns how many turn records were given one.
    /// </summary>
    /// <param name="keyFor">A folder's project key and name; null when it can't be told. Called outside the store's lock.</param>
    public int BackfillProjectKeys(Func<string, (string Key, string Name)?> keyFor)
    {
        var folders = Locked(() =>
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT project FROM turns WHERE machine IS NULL AND project IS NOT NULL AND project_key IS NULL";
            using var reader = command.ExecuteReader();
            var found = new List<string>();
            while (reader.Read())
            {
                found.Add(reader.GetString(0));
            }
            return found;
        });
        var keys = folders.Select(folder => (Folder: folder, Project: keyFor(folder))).Where(f => f.Project is not null).ToArray();
        if (keys.Length == 0)
        {
            return 0;
        }
        return Locked(() =>
        {
            using var transaction = _connection.BeginTransaction();
            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE turns SET project_key = $key, project_name = $name WHERE machine IS NULL AND project = $project AND project_key IS NULL";
            var updated = 0;
            foreach (var (folder, project) in keys)
            {
                update.Parameters.Clear();
                Add(update, "$key", project!.Value.Key);
                Add(update, "$name", project.Value.Name);
                Add(update, "$project", folder);
                updated += update.ExecuteNonQuery();
            }
            transaction.Commit();
            return updated;
        });
    }

    /// <summary>Turn records from <paramref name="from"/> to <paramref name="to"/> (both inclusive), oldest first.</summary>
    public IReadOnlyList<TurnRecord> GetTurns(DateTimeOffset from, DateTimeOffset to, string? tabId = null) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        // Two queries rather than "$tabId IS NULL OR tab_id = $tabId", which SQLite can't answer from the
        // tab's index (turns_by_tab), so it would read every turn in the range.
        command.CommandText = tabId is null
            ? """
              SELECT timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd, project, account, turn, idle_ms FROM turns
              WHERE timestamp >= $from AND timestamp <= $to
              ORDER BY timestamp, id
              """
            : """
              SELECT timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd, project, account, turn, idle_ms FROM turns
              WHERE tab_id = $tabId AND timestamp >= $from AND timestamp <= $to
              ORDER BY timestamp, id
              """;
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        Add(command, "$to", to.ToUnixTimeMilliseconds());
        if (tabId is not null)
        {
            Add(command, "$tabId", tabId);
        }
        using var reader = command.ExecuteReader();
        var turns = new List<TurnRecord>();
        while (reader.Read())
        {
            turns.Add(new TurnRecord(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10))
            {
                Turn = NullableString(reader, 11),
                IdleBefore = reader.IsDBNull(12) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(12)),
            });
        }
        return turns;
    });

    /// <summary>
    /// Keeps <paramref name="name"/> as the tab's name, so its turns are still named once it's closed. Nothing is stored
    /// for a tab without turn records.
    /// </summary>
    public void SetTabName(string tabId, string name) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tabs (tab_id, name) SELECT $tabId, $name WHERE EXISTS (SELECT 1 FROM turns WHERE tab_id = $tabId)
            ON CONFLICT (tab_id) DO UPDATE SET name = excluded.name
            """;
        Add(command, "$tabId", tabId);
        Add(command, "$name", name);
        command.ExecuteNonQuery();
    });

    /// <summary>
    /// Tokens per project and machine since <paramref name="from"/>, the heaviest first; turns without a project together.
    /// A project's turns go together by its key, whichever of its folders (a worktree, another clone) they were in.
    /// </summary>
    public IReadOnlyList<ProjectTokenSum> GetTokensByProject(DateTimeOffset from) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        // A turn's records (one per model) share its timestamp and tab. Of a project's folders, the first by name is a
        // worktree's main checkout, which its worktrees are inside.
        command.CommandText = """
            SELECT MIN(project), project_key, MAX(project_name), machine,
                   SUM(input), SUM(output), SUM(cache_write), SUM(cache_read), SUM(cost_usd), COUNT(DISTINCT tab_id || '|' || timestamp)
            FROM turns
            WHERE timestamp >= $from
            GROUP BY COALESCE(project_key, project), machine
            ORDER BY SUM(input + output + cache_write + cache_read) DESC, COALESCE(project_key, project), machine
            """;
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var sums = new List<ProjectTokenSum>();
        while (reader.Read())
        {
            sums.Add(new ProjectTokenSum(
                NullableString(reader, 0),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetDouble(8),
                reader.GetInt32(9))
            {
                Key = NullableString(reader, 1),
                Name = NullableString(reader, 2),
                Machine = NullableString(reader, 3),
            });
        }
        return sums;
    });

    /// <summary>
    /// An account's estimated spend since <paramref name="from"/>, in US dollars (DESIGN.md §4, "Accounts"); null for the
    /// Default account. Claude Code's own estimates, at list price.
    /// </summary>
    /// <param name="name">
    /// The account's name: other machines' turns under an API-key account of that name count too (DESIGN.md §6, "Sharing
    /// across machines"). Null counts only this machine's.
    /// </param>
    public double GetSpend(string? account, DateTimeOffset from, string? name = null) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT TOTAL(cost_usd) FROM turns WHERE timestamp >= $from
            AND ((machine IS NULL AND account IS $account)
                 OR ($name IS NOT NULL AND machine IS NOT NULL AND account IS NOT NULL AND account_name = $name COLLATE NOCASE))
            """;
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        Add(command, "$account", account);
        Add(command, "$name", name?.Trim());
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    });

    /// <summary>
    /// Tokens per account and machine since <paramref name="from"/>, the heaviest first; the Default account's as null.
    /// Another machine's API-key account comes with its name, which matches it to this machine's.
    /// </summary>
    public IReadOnlyList<AccountTokenSum> GetTokensByAccount(DateTimeOffset from) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        // A turn's records (one per model) share its timestamp and tab.
        command.CommandText = """
            SELECT account, CASE WHEN machine IS NULL THEN NULL ELSE MAX(account_name) END, machine,
                   SUM(input), SUM(output), SUM(cache_write), SUM(cache_read), SUM(cost_usd), COUNT(DISTINCT tab_id || '|' || timestamp)
            FROM turns
            WHERE timestamp >= $from
            GROUP BY account, machine
            ORDER BY SUM(input + output + cache_write + cache_read) DESC, account, machine
            """;
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var sums = new List<AccountTokenSum>();
        while (reader.Read())
        {
            sums.Add(new AccountTokenSum(
                NullableString(reader, 0),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetDouble(7),
                reader.GetInt32(8))
            {
                AccountName = NullableString(reader, 1),
                Machine = NullableString(reader, 2),
            });
        }
        return sums;
    });

    /// <summary>Each tab's tokens since <paramref name="from"/>, the heaviest first, with its last known name.</summary>
    /// <param name="planOnly">
    /// Only the Default account's turns, which are the ones that use the plan: an API key's don't (DESIGN.md §4,
    /// "Accounts").
    /// </param>
    public IReadOnlyList<TabTokenSum> GetTokensByTab(DateTimeOffset from, bool planOnly = false) => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        // A turn's records (one per model) share its timestamp.
        command.CommandText = $"""
            SELECT turns.tab_id, SUM(input), SUM(output), SUM(cache_write), SUM(cache_read), SUM(cost_usd), COUNT(DISTINCT timestamp), tabs.name, turns.machine
            FROM turns LEFT JOIN tabs ON tabs.tab_id = turns.tab_id
            WHERE timestamp >= $from{(planOnly ? " AND account IS NULL" : "")}
            GROUP BY turns.tab_id, turns.machine
            ORDER BY SUM(input + output + cache_write + cache_read) DESC, turns.tab_id
            """;
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var sums = new List<TabTokenSum>();
        while (reader.Read())
        {
            sums.Add(new TabTokenSum(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetDouble(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7))
            {
                Machine = NullableString(reader, 8),
            });
        }
        return sums;
    });

    /// <summary>
    /// Deletes samples and turn records older than <paramref name="keepFor"/> (DESIGN.md §6, "Retention"), and the names
    /// of tabs with no turns left. Null keeps everything. Returns the number of samples and turn records deleted.
    /// </summary>
    public int Prune(TimeSpan? keepFor)
    {
        if (keepFor is not { } keep)
        {
            return 0;
        }
        return Locked(() =>
        {
            var cutoff = (_time.GetUtcNow() - keep).ToUnixTimeMilliseconds();
            using var transaction = _connection.BeginTransaction();
            var deleted = Execute(transaction, "DELETE FROM samples WHERE timestamp < $cutoff", cutoff)
                + Execute(transaction, "DELETE FROM turns WHERE timestamp < $cutoff", cutoff);
            DeleteDetail(transaction, "<", cutoff);
            Execute(transaction, "DELETE FROM tabs WHERE tab_id NOT IN (SELECT tab_id FROM turns)", null);
            Execute(transaction, "DELETE FROM machines WHERE id NOT IN (SELECT machine FROM turns WHERE machine IS NOT NULL)", null);
            transaction.Commit();
            return deleted;
        });
    }

    /// <summary>
    /// Deletes every sample, turn record and tab name ("Clear usage history"). Samples and turns other machines shared up
    /// to now aren't imported again. Returns that moment, to the millisecond, as <see cref="ClearedAt"/> has it.
    /// </summary>
    public DateTimeOffset Clear() => Locked(() =>
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        using (var transaction = _connection.BeginTransaction())
        {
            Execute(transaction, "DELETE FROM samples", null);
            Execute(transaction, "DELETE FROM turns", null);
            Execute(transaction, "DELETE FROM calls", null);
            Execute(transaction, "DELETE FROM steps", null);
            Execute(transaction, "DELETE FROM events", null);
            Execute(transaction, "DELETE FROM tabs", null);
            Execute(transaction, "DELETE FROM machines", null);
            Execute(transaction, "DELETE FROM imports", null);
            SetFloor(transaction, now);
            transaction.Commit();
        }
        // Give the space back and leave nothing of the old rows in the file.
        Execute(null, "VACUUM", null);
        _lastSaved = null;
        _lastWrite = null;
        return DateTimeOffset.FromUnixTimeMilliseconds(now);
    });

    /// <summary>When the history was last cleared, here or by <see cref="ClearThrough"/>; null if it never was.</summary>
    public DateTimeOffset? ClearedAt => Locked(() =>
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT through FROM imports WHERE machine = $every";
        Add(command, "$every", EveryMachine);
        return command.ExecuteScalar() is long through ? DateTimeOffset.FromUnixTimeMilliseconds(through) : (DateTimeOffset?)null;
    });

    /// <summary>
    /// Another machine cleared usage history on every machine at <paramref name="at"/> (DESIGN.md §6, "Clear usage
    /// history"): deletes the samples and turn records up to then, and the names left without turns, and imports nothing
    /// from before it again. What came after it stays. Returns the number of samples and turn records deleted.
    /// </summary>
    public int ClearThrough(DateTimeOffset at) => Locked(() =>
    {
        var through = at.ToUnixTimeMilliseconds();
        using var transaction = _connection.BeginTransaction();
        var deleted = Execute(transaction, "DELETE FROM samples WHERE timestamp <= $cutoff", through)
            + Execute(transaction, "DELETE FROM turns WHERE timestamp <= $cutoff", through);
        DeleteDetail(transaction, "<=", through);
        Execute(transaction, "DELETE FROM tabs WHERE tab_id NOT IN (SELECT tab_id FROM turns)", null);
        Execute(transaction, "DELETE FROM machines WHERE id NOT IN (SELECT machine FROM turns WHERE machine IS NOT NULL)", null);
        SetFloor(transaction, through);
        transaction.Commit();
        if (ReadLatestSample(ownOnly: true) is { } latest)
        {
            _lastSaved = SampleValues.From(latest);
            _lastWrite = latest.Timestamp;
        }
        else
        {
            _lastSaved = null;
            _lastWrite = null;
        }
        return deleted;
    });

    /// <summary>Deletes the calls, steps and events from before <paramref name="cutoff"/>, by <paramref name="comparison"/> (<c>&lt;</c> or <c>&lt;=</c>), as their turns go.</summary>
    private void DeleteDetail(SqliteTransaction transaction, string comparison, long cutoff)
    {
        Execute(transaction, $"DELETE FROM calls WHERE timestamp {comparison} $cutoff", cutoff);
        Execute(transaction, $"DELETE FROM steps WHERE timestamp {comparison} $cutoff", cutoff);
        Execute(transaction, $"DELETE FROM events WHERE timestamp {comparison} $cutoff", cutoff);
    }

    /// <summary>Nothing from before <paramref name="through"/> is imported again, nor before any later floor already set.</summary>
    private void SetFloor(SqliteTransaction transaction, long through)
    {
        using var floor = _connection.CreateCommand();
        floor.Transaction = transaction;
        floor.CommandText = "INSERT INTO imports (machine, through) VALUES ($every, $through) ON CONFLICT (machine) DO UPDATE SET through = MAX(through, excluded.through)";
        Add(floor, "$every", EveryMachine);
        Add(floor, "$through", through);
        floor.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _connection.Dispose();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the lock, on a store that isn't disposed. A damaged file found on the way is kept
    /// aside and a new one started (<see cref="StartAfresh"/>); the call that found it still fails.
    /// </summary>
    private T Locked<T>(Func<T> work)
    {
        lock (_lock)
        {
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return work();
            }
            catch (SqliteException ex) when (UsageSchema.IsCorrupt(ex))
            {
                StartAfresh(ex);
                throw;
            }
        }
    }

    /// <inheritdoc cref="Locked{T}"/>
    private void Locked(Action work) => Locked<object?>(() =>
    {
        work();
        return null;
    });

    private SqliteConnection OpenOrReplace()
    {
        try
        {
            return UsageSchema.Open(Path);
        }
        catch (SqliteException ex) when (UsageSchema.IsCorrupt(ex))
        {
            SetAside(ex);
            return UsageSchema.Open(Path);
        }
    }

    /// <summary>
    /// The file turned out damaged after it was opened: every later write would fail too, and the history would quietly
    /// stop. Keep it aside and carry on with a new one; the operation that found it still fails. Called under the lock.
    /// </summary>
    private void StartAfresh(SqliteException ex)
    {
        _connection.Dispose();
        SetAside(ex);
        _connection = UsageSchema.Open(Path);
        _lastSaved = default;
        _lastWrite = null;
    }

    /// <summary>Keeps a damaged file as <c>*.bad</c>, with its journal and write-ahead log, so none of them is rolled into the new one.</summary>
    private void SetAside(SqliteException ex)
    {
        var backup = $"{Path}.{_time.GetUtcNow():yyyyMMddHHmmss}.bad";
        _logger.LogWarning(ex, "Couldn't read the usage history {Path}; keeping it as {Backup} and starting a new one.", Path, backup);
        File.Move(Path, backup, overwrite: true);
        foreach (var suffix in (string[])["-journal", "-wal", "-shm"])
        {
            if (File.Exists(Path + suffix))
            {
                File.Move(Path + suffix, backup + suffix, overwrite: true);
            }
        }
    }

    private UsageSample? ReadLatestSample(bool ownOnly)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"{SelectSamples} {(ownOnly ? "WHERE machine IS NULL " : "")}ORDER BY timestamp DESC, id DESC LIMIT 1";
        return ReadSamples(command) is [var latest] ? latest : null;
    }

    private static List<UsageSample> ReadSamples(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var samples = new List<UsageSample>();
        while (reader.Read())
        {
            samples.Add(new UsageSample(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                reader.IsDBNull(5) ? [] : ModelsFromJson(reader.GetString(5))));
        }
        return samples;
    }

    private int Execute(SqliteTransaction? transaction, string sql, long? cutoff)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (cutoff is not null)
        {
            Add(command, "$cutoff", cutoff);
        }
        return command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Ids and names from a two-column query, only for <paramref name="ids"/> when given. Called under the lock.</summary>
    private Dictionary<string, string> ReadNames(string sql, IEnumerable<string>? ids)
    {
        var wanted = ids?.ToHashSet(StringComparer.Ordinal);
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (wanted is null || wanted.Contains(id))
            {
                names[id] = reader.GetString(1);
            }
        }
        return names;
    }

    private static string? ModelsToJson(IReadOnlyList<LimitReading> models)
    {
        if (models.Count == 0)
        {
            return null;
        }
        var array = new JsonArray();
        foreach (var model in models)
        {
            array.Add(new JsonObject
            {
                ["label"] = model.Label,
                ["percent"] = model.Percent,
                ["resetsAt"] = model.ResetsAt?.ToUnixTimeMilliseconds(),
            });
        }
        return array.ToJsonString();
    }

    private static IReadOnlyList<ModelSample> ModelsFromJson(string json)
    {
        try
        {
            return JsonTree.Parse(json) is JsonArray array
                ? [.. array.OfType<JsonObject>()
                    .Where(m => m.GetString("label") is not null && m.GetDouble("percent") is not null)
                    .Select(m => new ModelSample(
                        m.GetString("label")!,
                        m.GetDouble("percent")!.Value,
                        m.GetDouble("resetsAt") is { } resetsAt ? DateTimeOffset.FromUnixTimeMilliseconds((long)resetsAt) : null))]
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>What a sample holds, in stored form, to tell whether anything changed.</summary>
    private sealed record SampleValues(double? SessionPercent, long? SessionResetsAt, double? WeeklyPercent, long? WeeklyResetsAt, string? Models)
    {
        public static SampleValues From(UsageSnapshot snapshot) => new(
            snapshot.Session?.Percent,
            snapshot.Session?.ResetsAt?.ToUnixTimeMilliseconds(),
            snapshot.WeeklyAll?.Percent,
            snapshot.WeeklyAll?.ResetsAt?.ToUnixTimeMilliseconds(),
            ModelsToJson(snapshot.WeeklyModels));

        public static SampleValues From(UsageSample sample) => From(sample.ToSnapshot());
    }
}
