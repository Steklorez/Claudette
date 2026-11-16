using System.Globalization;
using Deventry.Core.Sessions;
using Deventry.Core.Transcripts;

namespace Deventry.Usage;

/// <summary>What checking a recorded turn against its session's transcript found (DESIGN.md §18, "Older turns").</summary>
public enum TurnCheckOutcome
{
    /// <summary>Its figures agree with the transcript's calls; a turn without calls has them now.</summary>
    Agreed = 0,

    /// <summary>They didn't, and the turn is corrected to the transcript, Claude Code's own record of each call.</summary>
    Corrected = 1,

    /// <summary>The transcript has a turn the usage history didn't record, which is added from it.</summary>
    Added = 2,

    /// <summary>Another machine's turn that doesn't agree: it's kept as that machine recorded it.</summary>
    Differs = 3,

    /// <summary>
    /// The transcript has no turn for it that ran when it did, or one recorded live with none of its calls: it's kept as
    /// recorded, and nothing is taken from the transcript.
    /// </summary>
    NotInTranscript = 4,
}

/// <summary>
/// A turn checked against its transcript, and what the usage history takes from it: the records to write (the turn's own
/// for one that agreed, corrected ones, or a new turn's), its calls, steps and compactions, read from the transcript, and
/// the check.
/// </summary>
/// <param name="Number">The turn's number as its breakdown had it; for an added turn, the number of the turn before it.</param>
/// <param name="Turn">Its id: its own, or a new one for a turn recorded before ids, or one added.</param>
/// <param name="Before">The turn's records before the check; empty for one added.</param>
/// <param name="Records">Its records after it: the same for one that agreed or differs.</param>
/// <param name="Detail">Its calls, steps and events to record, the check's event among them; only the event for one that already had its calls.</param>
public sealed record CheckedTurn(TurnCheckOutcome Outcome, int Number, string Turn, string? Machine, DateTimeOffset At, IReadOnlyList<TurnRecord> Before,
    IReadOnlyList<TurnRecord> Records, TurnDetail Detail, int Calls)
{
    public long Tokens => Records.Sum(r => r.Total);

    public double CostUsd => Records.Sum(r => r.CostUsd);
}

/// <summary>A session's transcript read for its tab's turns: each turn checked, and each one's prompt.</summary>
/// <param name="Prompts">Each turn's prompt, by its id: a turn recorded before ids by the one the check gives it.</param>
public sealed record SessionCheck(string SessionId, IReadOnlyList<CheckedTurn> Turns, IReadOnlyDictionary<string, string> Prompts);

/// <summary>
/// Checks a tab's recorded turns against their session's transcript (DESIGN.md §18, "Older turns: reading the
/// transcript"), turn by turn and by time: each recorded turn is the transcript's turn it ended in, the prompt before it to
/// the next. A turn that agrees and had no calls gets the transcript's; one that doesn't is corrected to them, its cost by
/// its model's price; a turn the transcript has between two recorded ones, which wasn't recorded (cut short when Deventry
/// closed), is added. Another machine's turns are only checked. The transcript's calls go through <see cref="TurnCalls"/>,
/// as the live stream's do, so a turn's calls add up to its records, Claude Code's own calls as one of their own.
/// </summary>
public static class TurnCheck
{
    /// <summary>A turn's end is recorded as Deventry hears its result, a moment after the transcript's last entry for it.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest a turn's last call can have ended before its result was recorded, and still be its: the result follows
    /// the main agent's last message straight away. A transcript turn whose calls ended earlier ran at another time.
    /// </summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// What a turn's records may hold beyond its calls in the transcript, per model, and still agree: Claude Code's own
    /// calls (DESIGN.md §18, "Spike"), and a compaction's call when it compacted.
    /// </summary>
    private const long OwnCallsAllowance = 50_000;

    /// <summary>
    /// Checks <paramref name="recorded"/>, a tab's turns in one session in time order, against that session's
    /// <paramref name="transcript"/>. Turns already checked (<see cref="TurnBreakdown.Check"/>) are only read past.
    /// </summary>
    /// <param name="prices">The tab's model prices, for what an added turn cost.</param>
    public static SessionCheck Check(string sessionId, IReadOnlyList<TurnBreakdown> recorded, IReadOnlyList<TranscriptTurn> transcript, TokenPrices prices,
        Func<string> newId)
    {
        var turns = recorded.OrderBy(t => t.At).ToList();
        // Each recorded turn's transcript turn: the last that started before it ended.
        var matched = new Dictionary<int, List<TurnBreakdown>>();
        var results = new List<CheckedTurn>();
        foreach (var turn in turns)
        {
            var index = -1;
            for (var i = 0; i < transcript.Count && (transcript[i].Start is null || transcript[i].Start <= turn.At + Slack); i++)
            {
                index = i;
            }
            if (index < 0 || transcript[index].End is null)
            {
                if (turn.Check is null)
                {
                    results.Add(NotFound(sessionId, turn, turn.Turn ?? newId()));
                }
                continue;
            }
            (matched.TryGetValue(index, out var list) ? list : matched[index] = []).Add(turn);
        }
        var first = matched.Count > 0 ? matched.Keys.Min() : int.MaxValue;
        var last = matched.Count > 0 ? matched.Keys.Max() : int.MinValue;

        var calls = new TurnCalls();
        var prompts = new Dictionary<string, string>(StringComparer.Ordinal);
        TurnBreakdown? previous = null;
        for (var i = 0; i < transcript.Count; i++)
        {
            var segment = transcript[i];
            if (!matched.TryGetValue(i, out var mine))
            {
                foreach (var item in segment.Items)
                {
                    TranscriptTurns.Feed(calls, item);
                }
                // Between two recorded turns, a turn the history doesn't have: cut short when Deventry closed. Before the
                // tab's first or after its last, it isn't the tab's (DESIGN.md §18, "Spike").
                if (i > first && i < last && segment.HasCalls && previous is not null)
                {
                    results.Add(Added(sessionId, previous, segment, calls, prices, newId()));
                }
                else
                {
                    calls.End(ModelUsageTotals.Empty, segment.End ?? DateTimeOffset.MinValue);
                }
                continue;
            }
            // A transcript turn several recorded turns ended in (a background task's report, say) is shared by time.
            var items = segment.Items.ToList();
            var at = 0;
            for (var k = 0; k < mine.Count; k++)
            {
                var turn = mine[k];
                var lastOfThem = k == mine.Count - 1;
                DateTimeOffset? time = null;
                while (at < items.Count)
                {
                    var next = items[at].Time ?? time;
                    if (!lastOfThem && next > turn.At + Slack)
                    {
                        break;
                    }
                    time = next;
                    TranscriptTurns.Feed(calls, items[at++]);
                }
                var id = turn.Turn ?? newId();
                if (segment.Prompt is { } prompt && k == 0)
                {
                    prompts[id] = prompt;
                }
                var checkedNow = Recorded(sessionId, turn, id, calls, prices).ToList();
                results.AddRange(checkedNow);
                // A turn is added only after one the transcript has: around one it doesn't, its turns can't be told apart.
                var found = checkedNow is [{ Outcome: not TurnCheckOutcome.NotInTranscript }] || (checkedNow.Count == 0 && turn.Check is not TurnCheckOutcome.NotInTranscript);
                previous = found ? turn : null;
            }
        }
        return new SessionCheck(sessionId, results, prompts);
    }

    /// <summary>A recorded turn, its transcript calls fed: checked, unless it was before.</summary>
    private static IEnumerable<CheckedTurn> Recorded(string sessionId, TurnBreakdown turn, string id, TurnCalls calls, TokenPrices prices)
    {
        var seen = calls.SoFar();
        var usage = UsageOf(turn.Records);
        if (turn.Check is not null)
        {
            calls.End(usage, turn.At);
            yield break;
        }
        // The transcript's part is the turn's only if the main agent's last call in it ended as the turn did, and a turn
        // recorded live has its calls in it: otherwise nothing is taken from it.
        var main = seen.Calls.Where(c => c.Agent is null).ToList();
        var live = turn.Calls.Select(c => c.MessageId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (main.Count == 0 || main.Max(c => c.At) is var last && (last > turn.At + Slack || last < turn.At - LongestWait)
            || (live.Count > 0 && !seen.Calls.Any(c => c.MessageId is { } message && live.Contains(message))))
        {
            calls.End(usage, turn.At);
            yield return NotFound(sessionId, turn, id);
            yield break;
        }
        var mine = TotalsOf(seen.Calls);
        var machine = turn.Records[0].Machine;
        var agrees = Agrees(mine, usage, seen.Compactions);
        if (agrees || machine is not null)
        {
            var log = calls.End(usage, turn.At);
            // A turn recorded live has its calls already; one recorded before them takes the transcript's.
            var detail = turn.HasCalls || machine is not null
                ? new TurnDetail([], [], [Checked(turn, id, sessionId, agrees ? TurnCheckOutcome.Agreed : TurnCheckOutcome.Differs)])
                : DetailOf(log, id, turn.TabId, sessionId, [Checked(turn, id, sessionId, TurnCheckOutcome.Agreed)]);
            // One recorded before calls has its idle time from the transcript too: the cache's age (DESIGN.md §18, "Findings").
            IReadOnlyList<TurnRecord> records = turn.HasCalls || machine is not null
                ? turn.Turn is null ? [.. turn.Records.Select(r => r with { Turn = id })] : turn.Records
                : [.. turn.Records.Select(r => r with { Turn = id, IdleBefore = r.IdleBefore ?? log.IdleBefore })];
            yield return new CheckedTurn(agrees ? TurnCheckOutcome.Agreed : TurnCheckOutcome.Differs, turn.Number, id, machine, turn.At, turn.Records, records, detail,
                turn.HasCalls ? turn.Calls.Count : log.Calls.Count);
            yield break;
        }
        // Corrected to the transcript's calls, each model's cost by its price, from the turn's own cost where it had one.
        var corrected = new Dictionary<string, UsageCounts>(StringComparer.Ordinal);
        foreach (var (model, counts) in usage.Models)
        {
            corrected[model] = counts;
        }
        foreach (var (model, tokens) in mine)
        {
            var was = usage.Models.GetValueOrDefault(model);
            var weight = WeighOf(was);
            var cost = was is { CostUsd: > 0 } && weight > 0 ? was.CostUsd * tokens.Weighted / weight : (prices.Input(model) ?? 0) * tokens.Weighted;
            corrected[model] = new UsageCounts(tokens.Input, tokens.Output, tokens.CacheWrite, tokens.CacheRead, cost);
        }
        var correctedLog = calls.End(new ModelUsageTotals(corrected), turn.At);
        var template = turn.Records[0];
        var newRecords = corrected.Select(m => turn.Records.FirstOrDefault(r => r.Model == m.Key) is { } record
                ? record with { Turn = id, Input = m.Value.Input, Output = m.Value.Output, CacheWrite = m.Value.CacheWrite, CacheRead = m.Value.CacheRead, CostUsd = m.Value.CostUsd,
                    IdleBefore = record.IdleBefore ?? correctedLog.IdleBefore }
                : template with { Turn = id, Model = m.Key, Input = m.Value.Input, Output = m.Value.Output, CacheWrite = m.Value.CacheWrite, CacheRead = m.Value.CacheRead, CostUsd = m.Value.CostUsd,
                    IdleBefore = template.IdleBefore ?? correctedLog.IdleBefore })
            .ToList();
        var check = Checked(turn, id, sessionId, TurnCheckOutcome.Corrected,
            string.Create(CultureInfo.InvariantCulture, $"{turn.Records.Sum(r => r.Total)} tokens, ${turn.Records.Sum(r => r.CostUsd):0.0000}"));
        yield return new CheckedTurn(TurnCheckOutcome.Corrected, turn.Number, id, null, turn.At, turn.Records, newRecords,
            DetailOf(correctedLog, id, turn.TabId, sessionId, [check]), correctedLog.Calls.Count);
    }

    /// <summary>A recorded turn the transcript doesn't have: kept as recorded, and checked, so it isn't looked for again.</summary>
    private static CheckedTurn NotFound(string sessionId, TurnBreakdown turn, string id) => new(TurnCheckOutcome.NotInTranscript, turn.Number, id,
        turn.Records[0].Machine, turn.At, turn.Records, turn.Turn is null ? [.. turn.Records.Select(r => r with { Turn = id })] : turn.Records,
        new TurnDetail([], [], [Checked(turn, id, sessionId, TurnCheckOutcome.NotInTranscript)]), turn.Calls.Count);

    /// <summary>A turn the transcript has and the usage history doesn't: its calls at their models' prices, as the turn after the one before it.</summary>
    private static CheckedTurn Added(string sessionId, TurnBreakdown before, TranscriptTurn segment, TurnCalls calls, TokenPrices prices, string id)
    {
        var seen = TotalsOf(calls.SoFar().Calls);
        var usage = new ModelUsageTotals(seen.ToDictionary(m => m.Key,
            m => new UsageCounts(m.Value.Input, m.Value.Output, m.Value.CacheWrite, m.Value.CacheRead, (prices.Input(m.Key) ?? 0) * m.Value.Weighted), StringComparer.Ordinal));
        var end = segment.End ?? before.At;
        var log = calls.End(usage, end);
        var template = before.Records[0];
        var records = usage.Models.Select(m => template with
        {
            Timestamp = end,
            SessionId = sessionId,
            Model = m.Key,
            Input = m.Value.Input,
            Output = m.Value.Output,
            CacheWrite = m.Value.CacheWrite,
            CacheRead = m.Value.CacheRead,
            CostUsd = m.Value.CostUsd,
            Turn = id,
            Index = null,
            IdleBefore = log.IdleBefore,
        }).ToList();
        var check = new UsageEvent(before.TabId, sessionId, id, end, UsageEventKind.Checked, (long)TurnCheckOutcome.Added);
        return new CheckedTurn(TurnCheckOutcome.Added, before.Number, id, null, end, [], records, DetailOf(log, id, before.TabId, sessionId, [check]), log.Calls.Count);
    }

    /// <summary>
    /// Every model the transcript's calls used is in the records, with as much of each kind or more, and no more beyond
    /// them than Claude Code's own calls and a compaction's account for.
    /// </summary>
    private static bool Agrees(IReadOnlyDictionary<string, CallTokens> calls, ModelUsageTotals records, IReadOnlyList<Compaction> compactions)
    {
        var compacted = compactions.Sum(c => c.PreTokens ?? 0);
        foreach (var (model, tokens) in calls)
        {
            if (!records.Models.TryGetValue(model, out var counts))
            {
                return false;
            }
            long[] rest = [counts.Input - tokens.Input, counts.Output - tokens.Output, counts.CacheWrite - tokens.CacheWrite, counts.CacheRead - tokens.CacheRead];
            var allowance = Math.Max(OwnCallsAllowance, tokens.Total) + (compactions.Count > 0 ? Math.Max(compacted, tokens.Total) : 0);
            if (rest.Any(r => r < 0) || rest.Sum() > allowance)
            {
                return false;
            }
        }
        return true;
    }

    private static Dictionary<string, CallTokens> TotalsOf(IEnumerable<ModelCall> calls) =>
        calls.GroupBy(c => c.Model, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Aggregate(CallTokens.Zero, (sum, c) => sum.Plus(c.Tokens)), StringComparer.Ordinal);

    private static ModelUsageTotals UsageOf(IReadOnlyList<TurnRecord> records) => new(records.GroupBy(r => r.Model, StringComparer.Ordinal).ToDictionary(g => g.Key,
        g => new UsageCounts(g.Sum(r => r.Input), g.Sum(r => r.Output), g.Sum(r => r.CacheWrite), g.Sum(r => r.CacheRead), g.Sum(r => r.CostUsd)), StringComparer.Ordinal));

    /// <summary>A record's tokens weighted as a call's are, its writes as five minutes'.</summary>
    private static double WeighOf(UsageCounts? counts) => counts is null ? 0
        : counts.Input + (counts.CacheRead * TokenPrices.Read) + (counts.CacheWrite * TokenPrices.Write5m) + (counts.Output * TokenPrices.Output);

    private static UsageEvent Checked(TurnBreakdown turn, string id, string sessionId, TurnCheckOutcome outcome, string? detail = null) =>
        new(turn.TabId, sessionId, id, turn.At, UsageEventKind.Checked, (long)outcome, detail) { Machine = turn.Records[0].Machine };

    /// <summary>A turn's calls, steps and compactions from the transcript, marked as such, and its check.</summary>
    private static TurnDetail DetailOf(TurnCallLog log, string id, string tabId, string sessionId, IReadOnlyList<UsageEvent> events) => new(
        [.. CallRecord.From(log, id, tabId, sessionId).Select(c => c with { Source = CallSource.Transcript })],
        StepRecord.From(log, id),
        [.. UsageEvent.From(log, id, tabId, sessionId), .. events]);
}
