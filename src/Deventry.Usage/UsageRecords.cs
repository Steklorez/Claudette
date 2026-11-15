using Deventry.Core.Sessions;

namespace Deventry.Usage;

/// <summary>A model-specific weekly limit within a <see cref="UsageSample"/>.</summary>
public sealed record ModelSample(string Label, double Percent, DateTimeOffset? ResetsAt);

/// <summary>
/// One stored plan-usage sample (DESIGN.md §6, "Usage history"). A percentage is null when the snapshot it came from
/// didn't have that limit.
/// </summary>
public sealed record UsageSample(
    DateTimeOffset Timestamp,
    double? SessionPercent,
    DateTimeOffset? SessionResetsAt,
    double? WeeklyPercent,
    DateTimeOffset? WeeklyResetsAt,
    IReadOnlyList<ModelSample> Models)
{
    /// <summary>The sample as a <see cref="UsageSource.Stored"/> snapshot, for the header after a restart.</summary>
    public UsageSnapshot ToSnapshot()
    {
        var limits = new List<LimitReading>();
        if (SessionPercent is { } session)
        {
            limits.Add(new LimitReading(LimitKind.Session, UsageParser.SessionLabel, session, SessionResetsAt, null, false));
        }
        if (WeeklyPercent is { } weekly)
        {
            limits.Add(new LimitReading(LimitKind.WeeklyAll, UsageParser.WeeklyLabel, weekly, WeeklyResetsAt, null, false));
        }
        limits.AddRange(Models.Select(m => new LimitReading(LimitKind.WeeklyModel, m.Label, m.Percent, m.ResetsAt, null, false)));
        return new UsageSnapshot(limits, Timestamp, UsageSource.Stored);
    }
}

/// <summary>
/// One model's tokens for one turn of one tab (DESIGN.md §6, "Usage history"). Counts only: no conversation content.
/// </summary>
/// <param name="CostUsd">Claude Code's client-side estimate at list price; not a bill.</param>
/// <param name="Project">The tab's folder, for usage by project; null for turns recorded before it was kept.</param>
/// <param name="Account">The account the tab ran under (DESIGN.md §4, "Accounts"); null for the Default account.</param>
public sealed record TurnRecord(
    DateTimeOffset Timestamp,
    string TabId,
    string? SessionId,
    string Model,
    long Input,
    long Output,
    long CacheWrite,
    long CacheRead,
    double CostUsd,
    string? Project = null,
    string? Account = null)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    /// <summary>The project's key (<c>Deventry.Core.Git.ProjectKey</c>), the same on other machines with a clone of it.</summary>
    public string? ProjectKey { get; init; }

    /// <summary>The project's folder name, which names it on a machine that doesn't have it.</summary>
    public string? ProjectName { get; init; }

    /// <summary>An API-key account's name, which other machines match their own accounts by.</summary>
    public string? AccountName { get; init; }

    /// <summary>For the Default account's turns, the plan account signed in (<see cref="UsageSharing.AccountKey"/>).</summary>
    public string? Plan { get; init; }

    /// <summary>
    /// The turn's id, which its records for each model share and its calls, steps and events point to (DESIGN.md §18,
    /// "What's recorded"); null for turns recorded before calls were.
    /// </summary>
    public string? Turn { get; init; }

    /// <summary>How long the tab's main agent had made no call before the turn's first: the prompt cache's age.</summary>
    public TimeSpan? IdleBefore { get; init; }

    /// <summary>
    /// One record per model a turn used, from its share of the result's running totals (<see cref="TurnCompleted.Usage"/>),
    /// as <c>TokenTotals.Add</c> counts it.
    /// </summary>
    public static IReadOnlyList<TurnRecord> From(ModelUsageTotals turn, string? sessionId, string tabId, DateTimeOffset timestamp, string? project = null,
        string? account = null) =>
        [.. turn.Models.Select(m => new TurnRecord(timestamp, tabId, sessionId, m.Key, m.Value.Input, m.Value.Output, m.Value.CacheWrite, m.Value.CacheRead,
            m.Value.CostUsd, project, account))];
}

/// <summary>Where a <see cref="CallRecord"/> came from. Stored as its number, so the numbers stay.</summary>
public enum CallSource
{
    /// <summary>Recorded as Claude Code reported it.</summary>
    Live = 0,

    /// <summary>Read from the session's transcript later (DESIGN.md §18, "Older turns: reading the transcript").</summary>
    Transcript = 1,
}

/// <summary>
/// One model call of a recorded turn (DESIGN.md §18, "What's recorded"): counts and names only, no conversation content.
/// Its <see cref="Kind"/> is stored as its number, so <see cref="CallKind"/>'s numbers stay.
/// </summary>
/// <param name="Turn">The turn's id (<see cref="TurnRecord.Turn"/>).</param>
/// <param name="Seq">Its place among the turn's calls, from 1.</param>
/// <param name="Agent">The subagent's tool call; null for the main agent.</param>
/// <param name="CostUsd">Its share of the turn's cost for its model: Claude Code's estimate at list price, not a bill.</param>
public sealed record CallRecord(
    string Turn,
    string TabId,
    string? SessionId,
    DateTimeOffset Timestamp,
    int Seq,
    string? MessageId,
    string? Agent,
    string? AgentType,
    string Model,
    long Input,
    long Output,
    long CacheRead,
    long Write5m,
    long Write1h,
    int WebSearches,
    int WebFetches,
    CallKind Kind,
    double CostUsd,
    CallSource Source = CallSource.Live)
{
    public long CacheWrite => Write5m + Write1h;

    public long Total => Input + Output + CacheRead + CacheWrite;

    /// <summary>The machine that shared it; null for this machine's.</summary>
    public string? Machine { get; init; }

    /// <summary>A turn's calls (<see cref="TurnCompleted.Calls"/>), to record with its turn.</summary>
    public static IReadOnlyList<CallRecord> From(TurnCallLog log, string turn, string tabId, string? sessionId) =>
        [.. log.Calls.Select(c => new CallRecord(turn, tabId, sessionId, c.At, c.Seq, c.MessageId, c.Agent, c.AgentType, c.Model, c.Tokens.Input,
            c.Tokens.Output, c.Tokens.CacheRead, c.Tokens.Write5m, c.Tokens.Write1h, c.WebSearches, c.WebFetches, c.Kind, c.CostUsd))];
}

/// <summary>A tool's result in a recorded turn: what it added to the conversation, in tokens, estimated from its size.</summary>
/// <param name="CallSeq">The call that asked for it (<see cref="CallRecord.Seq"/>).</param>
/// <param name="Agent">The subagent whose tool it was; null for the main agent's.</param>
public sealed record StepRecord(string Turn, int CallSeq, string? Agent, string Tool, long Tokens, bool IsError, DateTimeOffset Timestamp)
{
    /// <summary>The machine that shared it; null for this machine's.</summary>
    public string? Machine { get; init; }

    /// <summary>A turn's tool results (<see cref="TurnCallLog.Steps"/>), to record with its turn.</summary>
    public static IReadOnlyList<StepRecord> From(TurnCallLog log, string turn) =>
        [.. log.Steps.Select(s => new StepRecord(turn, s.CallSeq, s.Agent, s.Tool, s.Tokens, s.IsError, s.At))];
}

/// <summary>What a <see cref="UsageEvent"/> says happened. Stored as its number, so the numbers stay.</summary>
public enum UsageEventKind
{
    /// <summary>Claude Code compacted the conversation; <see cref="UsageEvent.Value"/> is the context's tokens before, when it said.</summary>
    Compacted = 0,

    /// <summary>The conversation was cleared: what was in it is no longer read.</summary>
    Cleared = 1,
}

/// <summary>Something that changed what a tab's later calls cost (DESIGN.md §18, "What's recorded").</summary>
/// <param name="Turn">The turn it happened in, when it was in one.</param>
/// <param name="Detail">More about it: a compaction's trigger, <c>auto</c> or <c>manual</c>.</param>
public sealed record UsageEvent(string TabId, string? SessionId, string? Turn, DateTimeOffset Timestamp, UsageEventKind Kind, long? Value = null, string? Detail = null)
{
    /// <summary>The machine that shared it; null for this machine's.</summary>
    public string? Machine { get; init; }

    /// <summary>A turn's compactions (<see cref="TurnCallLog.Compactions"/>), to record with its turn.</summary>
    public static IReadOnlyList<UsageEvent> From(TurnCallLog log, string turn, string tabId, string? sessionId) =>
        [.. log.Compactions.Select(c => new UsageEvent(tabId, sessionId, turn, c.At, UsageEventKind.Compacted, c.PreTokens, c.Trigger))];
}

/// <summary>A turn's calls, steps and events, as <see cref="UsageStore.AddTurns"/> records them with it.</summary>
public sealed record TurnDetail(IReadOnlyList<CallRecord> Calls, IReadOnlyList<StepRecord> Steps, IReadOnlyList<UsageEvent> Events)
{
    public static readonly TurnDetail None = new([], [], []);
}

/// <summary>A tab's tokens since some moment, for "which tab is burning the most".</summary>
/// <param name="Turns">Turns, not records: a turn that used two models counts once.</param>
/// <param name="Name">The tab's last known name; null for turns recorded before names were kept.</param>
public sealed record TabTokenSum(string TabId, long Input, long Output, long CacheWrite, long CacheRead, double CostUsd, int Turns, string? Name = null)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    /// <summary>The machine whose tab it is, when another one shared it (DESIGN.md §6, "Sharing across machines"); null for this machine's.</summary>
    public string? Machine { get; init; }
}

/// <summary>
/// A project's tokens over a period, for usage by project (DESIGN.md §6, "Usage history"): one machine's, so the
/// Usage panel can add up every machine's and say how much each did.
/// </summary>
/// <param name="Project">This machine's folder for it; null for another machine's turns, and turns recorded before projects were kept.</param>
/// <param name="Turns">Turns, not records: a turn that used two models counts once.</param>
public sealed record ProjectTokenSum(string? Project, long Input, long Output, long CacheWrite, long CacheRead, double CostUsd, int Turns)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    /// <summary>The project's key, which every machine's turns in it share; null for turns recorded before keys were kept.</summary>
    public string? Key { get; init; }

    /// <summary>The project's folder name as the turns' machine recorded it.</summary>
    public string? Name { get; init; }

    /// <summary>The machine that shared these turns; null for this machine's.</summary>
    public string? Machine { get; init; }
}

/// <summary>An account's tokens over a period (DESIGN.md §4, "Accounts").</summary>
/// <param name="Account">The account's id on the machine whose turns these are; null for the Default account.</param>
/// <param name="Turns">Turns, not records: a turn that used two models counts once.</param>
public sealed record AccountTokenSum(string? Account, long Input, long Output, long CacheWrite, long CacheRead, double CostUsd, int Turns)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    /// <summary>The account's name, for another machine's API-key account: this machine's account of that name is the same one.</summary>
    public string? AccountName { get; init; }

    /// <summary>The machine that shared these turns; null for this machine's.</summary>
    public string? Machine { get; init; }
}

/// <summary>A plan window of the usage history: the 5-hour session or the week across all models.</summary>
public enum UsageWindow
{
    Session,
    Weekly,
}

/// <summary>A past window and the highest usage it reached.</summary>
public sealed record WindowPeak(DateTimeOffset ResetsAt, double PeakPercent);
