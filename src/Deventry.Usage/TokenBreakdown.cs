using Deventry.Core.Sessions;

namespace Deventry.Usage;

/// <summary>Tokens of the four kinds every view splits them into (DESIGN.md §18, "Kinds of token").</summary>
public sealed record TokenKinds(long CacheRead, long CacheWrite, long Input, long Output)
{
    public static readonly TokenKinds Zero = new(0, 0, 0, 0);

    public long Total => CacheRead + CacheWrite + Input + Output;

    /// <summary>The share of what Claude read that came from the prompt cache: 0 to 1, or null when it read nothing.</summary>
    public double? CacheShare => CacheRead + CacheWrite + Input is var read and > 0 ? (double)CacheRead / read : null;

    /// <summary>What the main agent's call read and wrote: how full its context was after it.</summary>
    public long Context => Total;

    public TokenKinds Plus(TokenKinds other) =>
        new(CacheRead + other.CacheRead, CacheWrite + other.CacheWrite, Input + other.Input, Output + other.Output);

    public static TokenKinds Of(CallRecord call) => new(call.CacheRead, call.CacheWrite, call.Input, call.Output);

    public static TokenKinds Of(TurnRecord turn) => new(turn.CacheRead, turn.CacheWrite, turn.Input, turn.Output);
}

/// <summary>What the four kinds cost, in US dollars: Claude Code's estimates at list price, not a bill.</summary>
public sealed record KindCosts(double CacheRead, double CacheWrite, double Input, double Output)
{
    public static readonly KindCosts Zero = new(0, 0, 0, 0);

    public double Total => CacheRead + CacheWrite + Input + Output;

    public KindCosts Plus(KindCosts other) =>
        new(CacheRead + other.CacheRead, CacheWrite + other.CacheWrite, Input + other.Input, Output + other.Output);

    /// <summary>A call's cost, shared between its kinds by their weighted tokens (DESIGN.md §18, "Prices").</summary>
    public static KindCosts Of(CallRecord call) =>
        Split(call.CostUsd, call.CacheRead * TokenPrices.Read, (call.Write5m * TokenPrices.Write5m) + (call.Write1h * TokenPrices.Write1h), call.Input, call.Output * TokenPrices.Output);

    /// <summary>
    /// A turn's cost by kind, for a turn recorded before its calls were: its writes' lifetime isn't known, so they weigh as
    /// five minutes'.
    /// </summary>
    public static KindCosts Of(TurnRecord turn) =>
        Split(turn.CostUsd, turn.CacheRead * TokenPrices.Read, turn.CacheWrite * TokenPrices.Write5m, turn.Input, turn.Output * TokenPrices.Output);

    private static KindCosts Split(double cost, double read, double write, double input, double output)
    {
        var weight = read + write + input + output;
        return weight <= 0 ? new KindCosts(0, 0, cost, 0) : new KindCosts(cost * read / weight, cost * write / weight, cost * input / weight, cost * output / weight);
    }
}

/// <summary>
/// What tokens cost, from Claude Code's own figures (DESIGN.md §18, "Prices"): a model's price for fresh input is its calls'
/// cost ÷ their tokens weighted as Anthropic prices every Claude model, which give every other kind's.
/// </summary>
public sealed class TokenPrices
{
    public const double Read = 0.1;
    public const double Write5m = 1.25;
    public const double Write1h = 2.0;
    public const double Output = 5.0;

    private readonly Dictionary<string, double> _input;

    private TokenPrices(Dictionary<string, double> input) => _input = input;

    /// <summary>Each model's price, from its calls, or from its turns where it has none.</summary>
    public static TokenPrices From(IEnumerable<CallRecord> calls, IEnumerable<TurnRecord> turns)
    {
        var sums = new Dictionary<string, (double Cost, double Weight)>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var (cost, weight) = sums.GetValueOrDefault(call.Model);
            sums[call.Model] = (cost + call.CostUsd, weight + Weigh(call));
        }
        foreach (var turn in turns.Where(t => !sums.ContainsKey(t.Model)).ToList())
        {
            var (cost, weight) = sums.GetValueOrDefault(turn.Model);
            sums[turn.Model] = (cost + turn.CostUsd, weight + turn.Input + (turn.CacheRead * Read) + (turn.CacheWrite * Write5m) + (turn.Output * Output));
        }
        return new TokenPrices(sums.Where(s => s.Value.Weight > 0 && s.Value.Cost > 0).ToDictionary(s => s.Key, s => s.Value.Cost / s.Value.Weight, StringComparer.Ordinal));
    }

    /// <summary>A token of fresh input on <paramref name="model"/>, in US dollars; null before it has had a cost.</summary>
    public double? Input(string model) => _input.TryGetValue(model, out var price) ? price : null;

    /// <summary>A token read from the cache on <paramref name="model"/>.</summary>
    public double? CacheRead(string model) => Input(model) * Read;

    /// <summary>A token written to the cache on <paramref name="model"/>, for an hour or five minutes.</summary>
    public double? CacheWrite(string model, bool hour) => Input(model) * (hour ? Write1h : Write5m);

    /// <summary>A token of output on <paramref name="model"/>.</summary>
    public double? OutputPrice(string model) => Input(model) * Output;

    /// <summary>A call's tokens weighted against fresh input.</summary>
    public static double Weigh(CallRecord call) =>
        call.Input + (call.CacheRead * Read) + (call.Write5m * Write5m) + (call.Write1h * Write1h) + (call.Output * Output);
}

/// <summary>One turn of a tab, with its calls and its tools' results (DESIGN.md §18, "The window").</summary>
/// <param name="Turn">Its id; null for a turn recorded before calls were.</param>
/// <param name="Number">Its place among its tab's turns, from 1.</param>
/// <param name="At">When it ended.</param>
/// <param name="Records">Its records, one per model it used, as the usage history keeps them.</param>
public sealed record TurnBreakdown(string? Turn, int Number, string TabId, DateTimeOffset At, IReadOnlyList<TurnRecord> Records, TokenKinds Tokens, KindCosts Costs,
    IReadOnlyList<CallRecord> Calls, IReadOnlyList<StepRecord> Steps, TimeSpan? IdleBefore, bool Compacted)
{
    public double CostUsd => Costs.Total;

    /// <summary>The models it used.</summary>
    public IReadOnlyList<string> Models => [.. Records.Select(r => r.Model).Distinct(StringComparer.Ordinal)];

    /// <summary>Its calls are recorded. Without them, only its totals are known.</summary>
    public bool HasCalls => Calls.Count > 0;

    /// <summary>The main agent's calls Claude made: not compaction's or Claude Code's own.</summary>
    public int MainCalls => Calls.Count(c => c.Agent is null && c.Kind is CallKind.Call or CallKind.CacheExpired);

    public int SubagentCalls => Calls.Count(c => c.Agent is not null);

    public int ToolCalls => Steps.Count;

    /// <summary>Its first call found the prompt cache expired.</summary>
    public bool CacheExpired => Calls.Any(c => c.Kind == CallKind.CacheExpired);

    /// <summary>The subagents' types, in the order they first called, *Explore*; a subagent without one is "".</summary>
    public IReadOnlyList<string> AgentTypes => [.. Calls.Where(c => c.Agent is not null).DistinctBy(c => c.Agent).Select(c => c.AgentType ?? "")];

    /// <summary>What checking it against its transcript found (DESIGN.md §18, "Older turns"); null before it's been checked.</summary>
    public TurnCheckOutcome? Check { get; init; }

    /// <summary>Its calls were read from the transcript rather than recorded as Claude Code reported them.</summary>
    public bool FromTranscript => Calls.Any(c => c.Source == CallSource.Transcript);

    /// <summary>How full the main agent's context was after its last call, or null without one.</summary>
    public long? ContextAfter => Calls.LastOrDefault(c => c.Agent is null && c.Kind is CallKind.Call or CallKind.CacheExpired) is { } last ? TokenKinds.Of(last).Context : null;
}

/// <summary>A model's share of a breakdown.</summary>
/// <param name="Calls">Its calls, Claude Code's own among them; 0 when only turns without calls used it.</param>
public sealed record ModelBreakdown(string Model, int Calls, TokenKinds Tokens, KindCosts Costs)
{
    public double CostUsd => Costs.Total;
}

/// <summary>Who made a breakdown's calls.</summary>
public enum AgentKind
{
    /// <summary>The main conversation, compaction included.</summary>
    Main,

    /// <summary>A subagent, started by one of the main agent's (or another subagent's) tool calls.</summary>
    Subagent,

    /// <summary>Claude Code's own calls, which it doesn't show (DESIGN.md §18, "Spike").</summary>
    ClaudeCode,
}

/// <summary>An agent's share of a breakdown.</summary>
/// <param name="Agent">A subagent's tool call; null for the main conversation and Claude Code's own.</param>
/// <param name="Type">A subagent's type, <c>Explore</c>.</param>
/// <param name="Turn">The turn a subagent ran in, by its number.</param>
/// <param name="ReportTokens">What a subagent's report added to the main conversation.</param>
public sealed record AgentBreakdown(AgentKind Kind, string? Agent, string? Type, int? Turn, IReadOnlyList<string> Models, int Calls, TokenKinds Tokens, KindCosts Costs, long? ReportTokens)
{
    public double CostUsd => Costs.Total;
}

/// <summary>
/// A tool's results: what they added to the conversation, and what later calls read again because of them, as each stays
/// in the context until it's compacted or cleared (DESIGN.md §18, "What's recorded").
/// </summary>
public sealed record ToolBreakdown(string Tool, int Calls, long Added, long ReadAgain, double ReadAgainCostUsd);

/// <summary>
/// A tab's part of a period's breakdown (DESIGN.md §18, "The Usage panel"), with what its latest record says of it: the
/// machine that ran it (null for this one), its session, its project and its account (null for the Default account).
/// </summary>
/// <param name="CacheExpired">Its turns whose first call found the prompt cache expired.</param>
public sealed record TabBreakdown(string TabId, string? Machine, string? SessionId, string? Project, string? ProjectName, string? Account, string? AccountName,
    int Turns, TokenKinds Tokens, KindCosts Costs, int CacheExpired)
{
    public double CostUsd => Costs.Total;
}

/// <summary>
/// What a tab's tokens went on, or a period's across tabs (DESIGN.md §18, "The breakdown"): by kind, turn by turn and call
/// by call, by model, agent and tool, and what cost more than it needed to. Every view shows its part of one, so a figure
/// shown in two places is the same figure.
/// </summary>
public sealed class TokenBreakdown
{
    /// <summary>A subagent that cost this share of the tab or more is a finding.</summary>
    public const double CostlySubagentShare = 0.10;

    /// <summary>The result read again the most is a finding when it's this share of the tab's cache reads or more.</summary>
    public const double HeavyResultShare = 0.05;

    /// <summary>Claude Code's own calls in a turn are a finding when they cost this share of the tab or more.</summary>
    public const double ClaudeCodeShare = 0.05;

    /// <summary>How many of the main agent's calls either side of a compaction its finding averages.</summary>
    private const int CallsAroundCompaction = 10;

    private TokenBreakdown(IReadOnlyList<TurnBreakdown> turns, TokenPrices prices, IReadOnlyList<UsageEvent> events)
    {
        Turns = turns;
        Prices = prices;
        Tokens = turns.Aggregate(TokenKinds.Zero, (sum, t) => sum.Plus(t.Tokens));
        Costs = turns.Aggregate(KindCosts.Zero, (sum, t) => sum.Plus(t.Costs));
        var calls = turns.SelectMany(t => t.Calls).ToList();
        ByModel = ModelsOf(turns);
        ByAgent = AgentsOf(turns);
        var reads = _reads = ReadsAgain(turns, events);
        ByTool = [.. turns.SelectMany(t => t.Steps).GroupBy(s => s.Tool, StringComparer.Ordinal)
            .Select(g => new ToolBreakdown(g.Key, g.Count(), g.Sum(s => s.Tokens), g.Sum(s => reads.GetValueOrDefault(s).Tokens), g.Sum(s => reads.GetValueOrDefault(s).CostUsd)))
            .OrderByDescending(t => t.ReadAgain).ThenByDescending(t => t.Added)];
        Findings = FindingsOf(turns, reads, events);
        CallCount = calls.Count;
        ByTab = TabsOf(turns);
        var expired = calls.Where(c => c.Kind == CallKind.CacheExpired).ToList();
        CacheExpired = (expired.Count, expired.Sum(c => c.CostUsd));
    }

    private readonly Dictionary<StepRecord, ReadAgain> _reads;

    public IReadOnlyList<TurnBreakdown> Turns { get; }

    /// <summary>How many later calls read a tool result again, and its tokens times them; nothing when none did.</summary>
    public (int Calls, long Tokens) ReadAgainOf(StepRecord step) => _reads.TryGetValue(step, out var read) ? (read.Calls, read.Tokens) : (0, 0);

    public TokenKinds Tokens { get; }

    public KindCosts Costs { get; }

    public double CostUsd => Costs.Total;

    /// <summary>Every call, Claude Code's own and compaction's included.</summary>
    public int CallCount { get; }

    /// <summary>The heaviest first, by cost.</summary>
    public IReadOnlyList<ModelBreakdown> ByModel { get; }

    /// <summary>The main conversation first, then the subagents, the costliest first, then Claude Code's own.</summary>
    public IReadOnlyList<AgentBreakdown> ByAgent { get; }

    /// <summary>The tools whose results were read again the most first.</summary>
    public IReadOnlyList<ToolBreakdown> ByTool { get; }

    /// <summary>What cost more than it needed to, the costliest first (DESIGN.md §18, "The breakdown").</summary>
    public IReadOnlyList<TokenFinding> Findings { get; }

    /// <summary>Each tab's part, the costliest first: one for a tab's own breakdown, every tab's for a period's.</summary>
    public IReadOnlyList<TabBreakdown> ByTab { get; }

    /// <summary>
    /// The turns whose first call found the prompt cache expired, and what those calls cost: reading each conversation
    /// again (DESIGN.md §18, "The Usage panel").
    /// </summary>
    public (int Turns, double CostUsd) CacheExpired { get; }

    public TokenPrices Prices { get; }

    /// <summary>
    /// A breakdown of <paramref name="turns"/> with their calls, steps and events, as the usage history has them. A turn's
    /// records for each model are one turn: by its id, or, for one recorded before ids, by its tab and time. Each turn is
    /// numbered among its tab's turns in <paramref name="turns"/>; <paramref name="from"/> leaves out those before it, so a
    /// period's turns keep their numbers.
    /// </summary>
    public static TokenBreakdown From(IReadOnlyList<TurnRecord> turns, IReadOnlyList<CallRecord> calls, IReadOnlyList<StepRecord> steps, IReadOnlyList<UsageEvent> events,
        DateTimeOffset? from = null)
    {
        var callsOf = calls.GroupBy(c => c.Turn, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Seq).ToList(), StringComparer.Ordinal);
        var stepsOf = steps.GroupBy(s => s.Turn, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Timestamp).ToList(), StringComparer.Ordinal);
        var compactedIn = events.Where(e => e.Kind == UsageEventKind.Compacted && e.Turn is not null).Select(e => e.Turn!).ToHashSet(StringComparer.Ordinal);
        var checks = events.Where(e => e.Kind == UsageEventKind.Checked && e.Turn is not null && e.Value is { } value && Enum.IsDefined((TurnCheckOutcome)value))
            .GroupBy(e => e.Turn!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (TurnCheckOutcome)g.MaxBy(e => e.Timestamp)!.Value!.Value, StringComparer.Ordinal);
        var built = new List<TurnBreakdown>();
        foreach (var tab in turns.GroupBy(t => t.TabId, StringComparer.Ordinal))
        {
            var place = 0;
            var number = 0;
            foreach (var turn in tab.GroupBy(t => t.Turn ?? $"{t.TabId}@{t.Timestamp.ToUnixTimeMilliseconds()}", StringComparer.Ordinal).OrderBy(g => g.Min(t => t.Timestamp)))
            {
                place++;
                var records = turn.ToList();
                // The number its card says, when it was recorded with one; otherwise its place. Each is after the one
                // before it: a turn added from the transcript moves those after it on by one (DESIGN.md §18, "Older turns").
                number = Math.Max(records[0].Index ?? place, number + 1);
                var id = records[0].Turn;
                var mine = id is not null && callsOf.TryGetValue(id, out var c) ? c : [];
                var tools = id is not null && stepsOf.TryGetValue(id, out var s) ? s : [];
                // A turn's calls add up to its records (DESIGN.md §18, "Spike"): by them when it has them, which split its writes.
                var tokens = mine.Count > 0 ? mine.Aggregate(TokenKinds.Zero, (sum, call) => sum.Plus(TokenKinds.Of(call))) : records.Aggregate(TokenKinds.Zero, (sum, r) => sum.Plus(TokenKinds.Of(r)));
                var costs = mine.Count > 0 ? mine.Aggregate(KindCosts.Zero, (sum, call) => sum.Plus(KindCosts.Of(call))) : records.Aggregate(KindCosts.Zero, (sum, r) => sum.Plus(KindCosts.Of(r)));
                var at = records.Max(r => r.Timestamp);
                if (from is { } start && at < start)
                {
                    continue;
                }
                built.Add(new TurnBreakdown(id, number, tab.Key, at, records, tokens, costs, mine, tools,
                    records[0].IdleBefore, mine.Any(call => call.Kind == CallKind.Compaction) || (id is not null && compactedIn.Contains(id)))
                {
                    Check = id is not null && checks.TryGetValue(id, out var check) ? check : null,
                });
            }
        }
        var ordered = built.OrderBy(t => t.At).ThenBy(t => t.TabId, StringComparer.Ordinal).ThenBy(t => t.Number).ToList();
        return new TokenBreakdown(ordered, TokenPrices.From(calls, turns), events);
    }

    /// <summary>
    /// A tab's breakdown from the usage history, its turns between <paramref name="from"/> and <paramref name="to"/>, each
    /// numbered among all the tab's turns. Synchronous: keep it off the UI thread.
    /// </summary>
    public static TokenBreakdown Read(UsageStore store, string tabId, DateTimeOffset from, DateTimeOffset to)
    {
        var turns = store.GetTurns(DateTimeOffset.MinValue, to, tabId);
        var inPeriod = turns.Where(t => t.Timestamp >= from).Select(t => t.Turn).OfType<string>().ToHashSet(StringComparer.Ordinal);
        // A turn's calls start before it ends, by as long as it ran: read them from the start of the earliest one.
        var calls = store.GetCalls(DateTimeOffset.MinValue, to, tabId).Where(c => inPeriod.Contains(c.Turn)).ToList();
        return From(turns, calls, store.GetSteps(inPeriod), store.GetEvents(DateTimeOffset.MinValue, to, tabId), from);
    }

    /// <summary>
    /// How long before a period's first turn ended its calls are read from: a turn's calls start before it ends, by as long
    /// as it ran.
    /// </summary>
    private static readonly TimeSpan LongestTurn = TimeSpan.FromDays(1);

    /// <summary>Every tab's turns between <paramref name="from"/> and <paramref name="to"/>, for the Usage panel. Synchronous: keep it off the UI thread.</summary>
    public static TokenBreakdown ReadPeriod(UsageStore store, DateTimeOffset from, DateTimeOffset to)
    {
        var turns = store.GetTurns(from, to);
        var ids = turns.Select(t => t.Turn).OfType<string>().ToHashSet(StringComparer.Ordinal);
        // Read from a day before the period, not the whole history: a year of it is millions of calls.
        var start = from == DateTimeOffset.MinValue ? from : from - LongestTurn;
        var calls = store.GetCalls(start, to).Where(c => ids.Contains(c.Turn)).ToList();
        return From(turns, calls, store.GetSteps(ids), store.GetEvents(start, to));
    }

    private static List<TabBreakdown> TabsOf(IReadOnlyList<TurnBreakdown> turns) =>
    [
        .. turns.GroupBy(t => t.TabId, StringComparer.Ordinal).Select(tab =>
        {
            var latest = tab.SelectMany(t => t.Records).MaxBy(r => r.Timestamp)!;
            return new TabBreakdown(tab.Key, latest.Machine, latest.SessionId, latest.Project, latest.ProjectName, latest.Account, latest.AccountName, tab.Count(),
                tab.Aggregate(TokenKinds.Zero, (sum, t) => sum.Plus(t.Tokens)), tab.Aggregate(KindCosts.Zero, (sum, t) => sum.Plus(t.Costs)), tab.Count(t => t.CacheExpired));
        }).OrderByDescending(t => t.CostUsd).ThenByDescending(t => t.Tokens.Total).ThenBy(t => t.TabId, StringComparer.Ordinal),
    ];

    private static List<ModelBreakdown> ModelsOf(IReadOnlyList<TurnBreakdown> turns)
    {
        var models = new Dictionary<string, (int Calls, TokenKinds Tokens, KindCosts Costs)>(StringComparer.Ordinal);
        foreach (var turn in turns)
        {
            if (turn.HasCalls)
            {
                foreach (var call in turn.Calls)
                {
                    var (count, tokens, costs) = models.GetValueOrDefault(call.Model, (0, TokenKinds.Zero, KindCosts.Zero));
                    models[call.Model] = (count + 1, tokens.Plus(TokenKinds.Of(call)), costs.Plus(KindCosts.Of(call)));
                }
            }
            else
            {
                // Recorded before its calls were: each model's record says what it spent.
                foreach (var record in turn.Records)
                {
                    var (count, tokens, costs) = models.GetValueOrDefault(record.Model, (0, TokenKinds.Zero, KindCosts.Zero));
                    models[record.Model] = (count, tokens.Plus(TokenKinds.Of(record)), costs.Plus(KindCosts.Of(record)));
                }
            }
        }
        return [.. models.Select(m => new ModelBreakdown(m.Key, m.Value.Calls, m.Value.Tokens, m.Value.Costs)).OrderByDescending(m => m.CostUsd).ThenByDescending(m => m.Tokens.Total)];
    }

    private static List<AgentBreakdown> AgentsOf(IReadOnlyList<TurnBreakdown> turns)
    {
        var main = (Calls: 0, Tokens: TokenKinds.Zero, Costs: KindCosts.Zero, Models: new HashSet<string>(StringComparer.Ordinal));
        var own = (Calls: 0, Tokens: TokenKinds.Zero, Costs: KindCosts.Zero, Models: new HashSet<string>(StringComparer.Ordinal));
        var subagents = new List<AgentBreakdown>();
        foreach (var turn in turns)
        {
            if (!turn.HasCalls)
            {
                main = (main.Calls, main.Tokens.Plus(turn.Tokens), main.Costs.Plus(turn.Costs), main.Models);
                main.Models.UnionWith(turn.Models);
                continue;
            }
            foreach (var call in turn.Calls.Where(c => c.Agent is null))
            {
                var tokens = TokenKinds.Of(call);
                var costs = KindCosts.Of(call);
                if (call.Kind == CallKind.ClaudeCode)
                {
                    own = (own.Calls + 1, own.Tokens.Plus(tokens), own.Costs.Plus(costs), own.Models);
                    own.Models.Add(call.Model);
                }
                else
                {
                    main = (main.Calls + 1, main.Tokens.Plus(tokens), main.Costs.Plus(costs), main.Models);
                    main.Models.Add(call.Model);
                }
            }
            foreach (var agent in turn.Calls.Where(c => c.Agent is not null).GroupBy(c => c.Agent!, StringComparer.Ordinal))
            {
                var report = turn.Steps.FirstOrDefault(s => s.ToolUseId == agent.Key)?.Tokens;
                subagents.Add(new AgentBreakdown(AgentKind.Subagent, agent.Key, agent.First().AgentType, turn.Number, [.. agent.Select(c => c.Model).Distinct(StringComparer.Ordinal)],
                    agent.Count(), agent.Aggregate(TokenKinds.Zero, (sum, c) => sum.Plus(TokenKinds.Of(c))), agent.Aggregate(KindCosts.Zero, (sum, c) => sum.Plus(KindCosts.Of(c))), report));
            }
        }
        var agents = new List<AgentBreakdown>();
        if (main.Tokens.Total > 0 || main.Calls > 0)
        {
            agents.Add(new AgentBreakdown(AgentKind.Main, null, null, null, [.. main.Models], main.Calls, main.Tokens, main.Costs, null));
        }
        agents.AddRange(subagents.OrderByDescending(a => a.CostUsd));
        if (own.Calls > 0)
        {
            agents.Add(new AgentBreakdown(AgentKind.ClaudeCode, null, null, null, [.. own.Models], own.Calls, own.Tokens, own.Costs, null));
        }
        return agents;
    }

    /// <summary>
    /// What later calls read again of each tool result: in its turn, the same agent's calls after the one that asked for it;
    /// for the main agent's, its calls in the turns after too, until the conversation is compacted or cleared. Calls are
    /// taken in their order, which a clock that hasn't moved between them doesn't change. Each is the result's tokens, at
    /// each call's model's price.
    /// </summary>
    private static Dictionary<StepRecord, ReadAgain> ReadsAgain(IReadOnlyList<TurnBreakdown> turns, IReadOnlyList<UsageEvent> events)
    {
        var reads = new Dictionary<StepRecord, ReadAgain>();
        var prices = TokenPrices.From(turns.SelectMany(t => t.Calls), []);
        foreach (var tab in turns.GroupBy(t => t.TabId, StringComparer.Ordinal))
        {
            var ordered = tab.OrderBy(t => t.At).ToList();
            var replaced = events.Where(e => e.TabId == tab.Key && e.Kind is UsageEventKind.Compacted or UsageEventKind.Cleared).OrderBy(e => e.Timestamp).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var turn = ordered[i];
                foreach (var step in turn.Steps)
                {
                    var until = step.Agent is null ? replaced.FirstOrDefault(e => e.Timestamp >= step.Timestamp) : null;
                    bool Before(CallRecord call) => until is null || call.Timestamp < until.Timestamp;
                    var later = turn.Calls.Where(c => c.Agent == step.Agent && c.Seq > step.CallSeq && c.Kind is CallKind.Call or CallKind.CacheExpired && Before(c)).ToList();
                    if (step.Agent is null)
                    {
                        later.AddRange(ordered.Skip(i + 1).SelectMany(t => t.Calls).Where(c => c.Agent is null && c.Kind is CallKind.Call or CallKind.CacheExpired && Before(c)));
                    }
                    if (later.Count == 0)
                    {
                        continue;
                    }
                    var cost = later.Sum(c => step.Tokens * (prices.CacheRead(c.Model) ?? 0));
                    reads[step] = new ReadAgain(later.Count, step.Tokens * later.Count, cost, until?.Kind);
                }
            }
        }
        return reads;
    }

    private List<TokenFinding> FindingsOf(IReadOnlyList<TurnBreakdown> turns, Dictionary<StepRecord, ReadAgain> reads, IReadOnlyList<UsageEvent> events)
    {
        var findings = new List<TokenFinding>();
        var total = CostUsd;
        var previous = new Dictionary<string, TurnBreakdown>(StringComparer.Ordinal);
        foreach (var turn in turns)
        {
            if (turn.Calls.FirstOrDefault(c => c.Kind == CallKind.CacheExpired) is { } expired)
            {
                findings.Add(new CacheExpiredFinding(turn.Number, previous.GetValueOrDefault(turn.TabId)?.Number, turn.IdleBefore, expired.Write1h > 0, expired.CacheWrite,
                    expired.CostUsd, Share(expired.CostUsd)));
            }
            foreach (var own in turn.Calls.Where(c => c.Kind == CallKind.ClaudeCode && Share(c.CostUsd) >= ClaudeCodeShare))
            {
                findings.Add(new ClaudeCodeFinding(turn.Number, own.Model, own.Total, own.CostUsd, turn.Steps.Any(s => s.Tool == "WebFetch")));
            }
            previous[turn.TabId] = turn;
        }

        foreach (var agent in ByAgent.Where(a => a.Kind == AgentKind.Subagent && Share(a.CostUsd) >= CostlySubagentShare))
        {
            var model = agent.Models.FirstOrDefault() ?? "";
            var cheaper = ByAgent.Where(a => a.Kind == AgentKind.Subagent && a.Type == agent.Type && a.Agent != agent.Agent && a.Calls > 0 && a.Models.FirstOrDefault() != model
                && a.CostUsd < agent.CostUsd).OrderBy(a => a.CostUsd).FirstOrDefault();
            findings.Add(new CostlySubagentFinding(agent.Turn ?? 0, agent.Type, agent.Calls, model, agent.Tokens.Total, agent.CostUsd, Share(agent.CostUsd), agent.ReportTokens,
                cheaper is null ? null : new CheaperRun(cheaper.Turn ?? 0, cheaper.Models.FirstOrDefault() ?? "", cheaper.CostUsd)));
        }

        if (reads.Count > 0 && reads.MaxBy(r => r.Value.Tokens) is var (step, read) && Tokens.CacheRead > 0 && (double)read.Tokens / Tokens.CacheRead >= HeavyResultShare)
        {
            var turn = turns.First(t => t.Steps.Contains(step));
            findings.Add(new HeavyResultFinding(turn.Number, step.Tool, step.Tokens, read.Calls, read.Tokens, read.CostUsd, read.Until) { ToolUseId = step.ToolUseId });
        }

        foreach (var turn in turns.Where(t => t.Compacted))
        {
            var compaction = turn.Calls.FirstOrDefault(c => c.Kind == CallKind.Compaction);
            var boundary = events.FirstOrDefault(e => e.Turn == turn.Turn && e.Kind == UsageEventKind.Compacted);
            var at = boundary?.Timestamp ?? compaction?.Timestamp ?? turn.At;
            var main = turns.Where(t => t.TabId == turn.TabId).SelectMany(t => t.Calls).Where(c => c.Agent is null && c.Kind is CallKind.Call or CallKind.CacheExpired).OrderBy(c => c.Timestamp).ToList();
            var before = main.Where(c => c.Timestamp < at).TakeLast(CallsAroundCompaction).ToList();
            var after = main.Where(c => c.Timestamp > at).Take(CallsAroundCompaction).ToList();
            findings.Add(new CompactionFinding(turn.Number, boundary?.Value ?? compaction?.CacheRead, after.FirstOrDefault() is { } next ? TokenKinds.Of(next).Context : null,
                before.Count > 0 ? before.Average(c => c.CostUsd) : null, after.Count > 0 ? after.Average(c => c.CostUsd) : null, compaction?.CostUsd ?? 0));
        }
        return [.. findings.OrderByDescending(f => f.CostUsd)];

        double Share(double cost) => total > 0 ? cost / total : 0;
    }

    private readonly record struct ReadAgain(int Calls, long Tokens, double CostUsd, UsageEventKind? Until);
}
