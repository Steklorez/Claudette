using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Core.Sessions;

/// <summary>A model call's tokens, as its <c>usage</c> reports them (DESIGN.md §18, "Token breakdown").</summary>
/// <param name="Input">Fresh input: neither read from the prompt cache nor written to it.</param>
/// <param name="Write5m">Written to the prompt cache for five minutes.</param>
/// <param name="Write1h">Written to the prompt cache for an hour.</param>
public sealed record CallTokens(long Input, long Output, long CacheRead, long Write5m, long Write1h)
{
    public static readonly CallTokens Zero = new(0, 0, 0, 0, 0);

    public long CacheWrite => Write5m + Write1h;

    public long Total => Input + Output + CacheRead + CacheWrite;

    /// <summary>
    /// The tokens weighted as Anthropic prices them for every Claude model, against fresh input: a cache read a tenth, a
    /// write 1.25 times for five minutes and twice for an hour, output five times. What a call's share of its turn's cost
    /// goes by (DESIGN.md §18, "Prices").
    /// </summary>
    public double Weighted => Input + (CacheRead * 0.1) + (Write5m * 1.25) + (Write1h * 2.0) + (Output * 5.0);

    public CallTokens Plus(CallTokens other) =>
        new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, Write5m + other.Write5m, Write1h + other.Write1h);

    /// <summary>A call's <c>usage</c>; null when the message carries none.</summary>
    public static CallTokens? From(JsonObject? usage)
    {
        if (usage is null)
        {
            return null;
        }
        var written = Number(usage["cache_creation_input_tokens"]);
        var (fiveMinutes, hour) = usage.GetObject("cache_creation") is { } creation
            ? (Number(creation["ephemeral_5m_input_tokens"]), Number(creation["ephemeral_1h_input_tokens"]))
            // Without the split, as the mock sends it, the write's lifetime isn't known: priced as five minutes.
            : (written, 0);
        return new CallTokens(Number(usage["input_tokens"]), Number(usage["output_tokens"]), Number(usage["cache_read_input_tokens"]), fiveMinutes, hour);
    }

    private static long Number(JsonNode? node) => node.AsWholeNumber() ?? 0;
}

/// <summary>
/// What kind of call a <see cref="ModelCall"/> is (DESIGN.md §18, "What's recorded"). The usage history stores the number,
/// so the numbers stay.
/// </summary>
public enum CallKind
{
    /// <summary>A call Claude made, as the stream showed it.</summary>
    Call = 0,

    /// <summary>The main agent's first call of a turn, which found the prompt cache expired and wrote the conversation again.</summary>
    CacheExpired = 1,

    /// <summary>Compacting the conversation: the turn's usage beyond its calls, when it compacted.</summary>
    Compaction = 2,

    /// <summary>Calls Claude Code made itself, which the stream doesn't show: the turn's usage beyond its calls.</summary>
    ClaudeCode = 3,
}

/// <summary>One model call of a turn (DESIGN.md §18, "What's recorded").</summary>
/// <param name="Seq">Its place among the turn's calls, from 1.</param>
/// <param name="MessageId">The <c>assistant</c> message's id; null for the calls the stream doesn't show.</param>
/// <param name="Agent">The subagent's tool call (its <c>parent_tool_use_id</c>); null for the main agent.</param>
/// <param name="AgentType">The subagent's type, such as Explore, from the tool call that started it.</param>
/// <param name="CostUsd">Its share of its turn's cost for its model, by its weighted tokens: Claude Code's estimate at list price.</param>
public sealed record ModelCall(int Seq, string? MessageId, string? Agent, string? AgentType, string Model, DateTimeOffset At, CallTokens Tokens,
    int WebSearches, int WebFetches, CallKind Kind, double CostUsd);

/// <summary>A tool's result: what it added to the conversation, in tokens, estimated from its size.</summary>
/// <param name="CallSeq">The call that asked for it, by <see cref="ModelCall.Seq"/>.</param>
/// <param name="Agent">The subagent whose tool it was; null for the main agent's.</param>
public sealed record ToolStep(int CallSeq, string? Agent, string Tool, long Tokens, bool IsError, DateTimeOffset At)
{
    /// <summary>The tool call's id: for an Agent call, the subagent's (its calls' <see cref="ModelCall.Agent"/>), whose report this is.</summary>
    public string? ToolUseId { get; init; }
}

/// <summary>Claude Code compacted the conversation during a turn.</summary>
/// <param name="PreTokens">The context's tokens before it, when Claude Code says.</param>
public sealed record Compaction(DateTimeOffset At, string? Trigger, long? PreTokens);

/// <summary>A turn's model calls, tool results and compactions (DESIGN.md §18, "What's recorded").</summary>
/// <param name="IdleBefore">How long the main agent had made no call before this turn's first: the prompt cache's age. Null for the process's first.</param>
public sealed record TurnCallLog(IReadOnlyList<ModelCall> Calls, IReadOnlyList<ToolStep> Steps, IReadOnlyList<Compaction> Compactions, TimeSpan? IdleBefore)
{
    public static readonly TurnCallLog Empty = new([], [], [], null);

    /// <summary>
    /// A turn cut short, with no result to price it (DESIGN.md §18, "Prices"): its calls each at <paramref name="price"/>
    /// (none for a call it can't price), and the usage by model they add up to, as a result's would be.
    /// </summary>
    public (TurnCallLog Log, ModelUsageTotals Usage) Priced(Func<ModelCall, double?> price)
    {
        var calls = Calls.Select(c => c with { CostUsd = price(c) ?? 0 }).ToList();
        var models = calls.GroupBy(c => c.Model, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new UsageCounts(
            g.Sum(c => c.Tokens.Input), g.Sum(c => c.Tokens.Output), g.Sum(c => c.Tokens.CacheWrite), g.Sum(c => c.Tokens.CacheRead), g.Sum(c => c.CostUsd)),
            StringComparer.Ordinal);
        var counted = models.Where(m => !m.Value.IsZero).ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
        return (this with { Calls = calls }, counted.Count == 0 ? ModelUsageTotals.Empty : new ModelUsageTotals(counted));
    }
}

/// <summary>
/// Gathers each turn's model calls from its <c>assistant</c> messages, the tool results between them and its compactions
/// (DESIGN.md §18, "Token breakdown"). Claude Code repeats a call's usage on each of its messages, so a call is counted once
/// by message id, with its latest figures. At the turn's end, what its <c>modelUsage</c> counts beyond its calls is a call
/// of its own, compaction's or Claude Code's, so the calls add up to the turn, and the turn's cost is shared between them by
/// their weighted tokens. Not thread-safe: the session's read loop is its only caller.
/// </summary>
public sealed class TurnCalls
{
    /// <summary>The model Claude Code gives its own messages, which no call made.</summary>
    private const string Synthetic = "<synthetic>";

    /// <summary>What an image the API is sent counts as, when its size can't be read.</summary>
    private const long ImageTokens = 1600;

    private readonly Dictionary<string, PendingCall> _byId = new(StringComparer.Ordinal);
    private readonly List<PendingCall> _calls = [];
    private readonly Dictionary<string, (int Seq, string? Agent, string Tool)> _toolUses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _agentTypes = new(StringComparer.Ordinal);
    private readonly List<ToolStep> _steps = [];
    private readonly List<Compaction> _compactions = [];

    private DateTimeOffset? _lastMainCall;
    private string? _lastMainModel;

    /// <summary>The conversation was compacted or cleared since the main agent's last call: its next call writes it all, as it should.</summary>
    private bool _contextReplaced;

    private TimeSpan? _idleBefore;

    /// <summary>The turn's first main call could find the cache expired: the call before it was the same model's, with nothing replaced since.</summary>
    private bool _mayFindCacheExpired;

    private int? _firstMainSeq;

    /// <summary>An <c>assistant</c> message: its call's usage, and the tools it asked for.</summary>
    public void Add(AssistantMessage message, DateTimeOffset at)
    {
        if (message.Model == Synthetic)
        {
            return;
        }
        var usage = message.Raw.GetObject("message")?.GetObject("usage");
        var key = message.MessageId ?? $"call-{_calls.Count + 1}";
        if (!_byId.TryGetValue(key, out var call))
        {
            if (usage is null || message.Model is null)
            {
                // Without its call's usage and model it counts as no call: its tools' results are kept, tied to none.
                foreach (var use in message.Content.OfType<ToolUseBlock>())
                {
                    _toolUses[use.Id] = (0, message.ParentToolUseId, use.Name);
                }
                return;
            }
            call = new PendingCall { Seq = _calls.Count + 1, MessageId = message.MessageId, Agent = message.ParentToolUseId, Model = message.Model, At = at };
            _byId[key] = call;
            _calls.Add(call);
            if (message.ParentToolUseId is null && _firstMainSeq is null)
            {
                _firstMainSeq = call.Seq;
                _idleBefore = _lastMainCall is { } last ? at - last : null;
                _mayFindCacheExpired = _lastMainCall is not null && !_contextReplaced && _lastMainModel == message.Model;
            }
        }
        if (usage is not null && CallTokens.From(usage) is { } tokens)
        {
            call.Tokens = tokens;
            call.WebSearches = (int)(usage.GetObject("server_tool_use")?["web_search_requests"].AsWholeNumber() ?? 0);
            call.WebFetches = (int)(usage.GetObject("server_tool_use")?["web_fetch_requests"].AsWholeNumber() ?? 0);
        }
        call.Model = message.Model ?? call.Model;
        foreach (var use in message.Content.OfType<ToolUseBlock>())
        {
            _toolUses[use.Id] = (call.Seq, message.ParentToolUseId, use.Name);
            if (use.Name is "Agent" or "Task" && use.Input.GetString("subagent_type") is { Length: > 0 } type)
            {
                _agentTypes[use.Id] = type;
            }
        }
        if (message.ParentToolUseId is null)
        {
            _lastMainCall = at;
            _lastMainModel = call.Model;
            _contextReplaced = false;
        }
    }

    /// <summary>A <c>user</c> message with tools' results: what each added to the conversation.</summary>
    public void AddResults(UserMessage message, DateTimeOffset at)
    {
        foreach (var result in message.Content.OfType<ToolResultBlock>())
        {
            if (!_toolUses.TryGetValue(result.ToolUseId, out var use))
            {
                continue;
            }
            var tokens = ((long)result.Text.Length + 3) / 4 + result.Images.Sum(TokensOf);
            _steps.Add(new ToolStep(use.Seq, message.ParentToolUseId ?? use.Agent, use.Tool, tokens, result.IsError, at) { ToolUseId = result.ToolUseId });
        }
    }

    /// <summary>Claude Code compacted the conversation.</summary>
    public void Compacted(CompactBoundaryNotice notice, DateTimeOffset at)
    {
        _compactions.Add(new Compaction(at, notice.Trigger, notice.PreTokens));
        _contextReplaced = true;
    }

    /// <summary>The conversation was cleared: its next call writes everything, as a first call does.</summary>
    public void Reset() => _contextReplaced = true;

    /// <summary>
    /// The turn so far, for what shows it while it runs (DESIGN.md §18, "The Tokens tool window"): its calls as the stream
    /// has shown them, without what only the turn's end says (the calls it didn't show, and each call's cost).
    /// </summary>
    public TurnCallLog SoFar() => new(
        [.. _calls.Select(c => new ModelCall(c.Seq, c.MessageId, c.Agent, c.Agent is { } agent ? _agentTypes.GetValueOrDefault(agent) : null, c.Model, c.At, c.Tokens,
            c.WebSearches, c.WebFetches, c.Seq == _firstMainSeq && _mayFindCacheExpired && c.Tokens.CacheWrite > c.Tokens.CacheRead ? CallKind.CacheExpired : CallKind.Call, 0))],
        [.. _steps], [.. _compactions], _idleBefore);

    /// <summary>
    /// The turn ended, having spent <paramref name="usage"/> (<see cref="TurnCompleted.Usage"/>): its calls, with what the
    /// stream didn't show as a call of its own, and each one's share of the cost. Starts the next turn afresh.
    /// </summary>
    public TurnCallLog End(ModelUsageTotals usage, DateTimeOffset at)
    {
        var calls = _calls.Select(c => new ModelCall(c.Seq, c.MessageId, c.Agent, c.Agent is { } agent ? _agentTypes.GetValueOrDefault(agent) : null, c.Model, c.At,
            c.Tokens, c.WebSearches, c.WebFetches,
            c.Seq == _firstMainSeq && _mayFindCacheExpired && c.Tokens.CacheWrite > c.Tokens.CacheRead ? CallKind.CacheExpired : CallKind.Call, 0)).ToList();

        // The turn's usage beyond its calls: compaction, and calls Claude Code makes itself (DESIGN.md §18, "Spike").
        foreach (var (model, counts) in usage.Models)
        {
            var seen = calls.Where(c => c.Model == model).Aggregate(CallTokens.Zero, (sum, c) => sum.Plus(c.Tokens));
            var written = Math.Max(0, counts.CacheWrite - seen.CacheWrite);
            var hour = seen.Write1h > 0 && seen.Write5m == 0;
            var rest = new CallTokens(Math.Max(0, counts.Input - seen.Input), Math.Max(0, counts.Output - seen.Output), Math.Max(0, counts.CacheRead - seen.CacheRead),
                hour ? 0 : written, hour ? written : 0);
            if (rest.Total > 0)
            {
                var kind = _compactions.Count > 0 && rest.CacheRead > 0 ? CallKind.Compaction : CallKind.ClaudeCode;
                calls.Add(new ModelCall(calls.Count + 1, null, null, null, model, at, rest, 0, 0, kind, 0));
            }
        }

        // Each model's cost for the turn, shared between its calls by their weighted tokens.
        foreach (var (model, counts) in usage.Models)
        {
            var mine = calls.Where(c => c.Model == model).ToList();
            var weight = mine.Sum(c => c.Tokens.Weighted);
            foreach (var c in mine)
            {
                var share = weight > 0 ? c.Tokens.Weighted / weight : 1.0 / mine.Count;
                calls[c.Seq - 1] = c with { CostUsd = counts.CostUsd * share };
            }
        }

        var log = calls.Count == 0 && _steps.Count == 0 && _compactions.Count == 0
            ? TurnCallLog.Empty with { IdleBefore = _idleBefore }
            : new TurnCallLog(calls, [.. _steps], [.. _compactions], _idleBefore);
        _byId.Clear();
        _calls.Clear();
        _toolUses.Clear();
        _agentTypes.Clear();
        _steps.Clear();
        _compactions.Clear();
        _firstMainSeq = null;
        _idleBefore = null;
        _mayFindCacheExpired = false;
        return log;
    }

    /// <summary>
    /// What an image counts as, as the Messages API counts it: its pixels ÷ 750, once its long side is down to 1,568. One
    /// whose size can't be read counts as the most one can.
    /// </summary>
    public static long TokensOf(MessageImage image)
    {
        if (ImageDimensions.Read(image.Data) is not { } size || size.Width <= 0 || size.Height <= 0)
        {
            return ImageTokens;
        }
        var scale = Math.Min(1.0, 1568.0 / Math.Max(size.Width, size.Height));
        return Math.Max(1, (long)Math.Ceiling(size.Width * scale * size.Height * scale / 750));
    }

    private sealed class PendingCall
    {
        public int Seq { get; init; }

        public string? MessageId { get; init; }

        public string? Agent { get; init; }

        public string Model { get; set; } = "";

        public DateTimeOffset At { get; init; }

        public CallTokens Tokens { get; set; } = CallTokens.Zero;

        public int WebSearches { get; set; }

        public int WebFetches { get; set; }
    }
}
