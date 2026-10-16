using Claudette.Core.Sessions;

namespace Claudette.Usage;

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

    /// <summary>The project's key (<c>Claudette.Core.Git.ProjectKey</c>), the same on other machines with a clone of it.</summary>
    public string? ProjectKey { get; init; }

    /// <summary>The project's folder name, which names it on a machine that doesn't have it.</summary>
    public string? ProjectName { get; init; }

    /// <summary>An API-key account's name, which other machines match their own accounts by.</summary>
    public string? AccountName { get; init; }

    /// <summary>For the Default account's turns, the plan account signed in (<see cref="UsageSharing.AccountKey"/>).</summary>
    public string? Plan { get; init; }

    /// <summary>
    /// One record per model a turn used, from its share of the result's running totals (<see cref="TurnCompleted.Usage"/>),
    /// as <c>TokenTotals.Add</c> counts it.
    /// </summary>
    public static IReadOnlyList<TurnRecord> From(ModelUsageTotals turn, string? sessionId, string tabId, DateTimeOffset timestamp, string? project = null,
        string? account = null) =>
        [.. turn.Models.Select(m => new TurnRecord(timestamp, tabId, sessionId, m.Key, m.Value.Input, m.Value.Output, m.Value.CacheWrite, m.Value.CacheRead,
            m.Value.CostUsd, project, account))];
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
