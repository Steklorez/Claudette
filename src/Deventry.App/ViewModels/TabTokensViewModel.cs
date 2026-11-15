using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deventry.App.Controls;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.ViewModels;

/// <summary>A call of the turn running, in the Tokens tool window, or the tool result after it.</summary>
public sealed record RunningCallRow(string Label, string What, KindValues? Values, double Scale, string Tokens, string Cost, bool IsStep);

/// <summary>One of the latest turns in the Tokens tool window, which opens the breakdown on it.</summary>
public sealed record LatestTurnRow(int Number, string? TurnId, string Prompt, TurnMark Mark, KindValues Values, double Scale, string Cost, bool IsRunning)
{
    public string NumberText => Number.ToString(CultureInfo.CurrentCulture);

    public bool IsCacheExpired => Mark == TurnMark.CacheExpired;
}

/// <summary>
/// A tab's tokens as the views beside its conversation show them (DESIGN.md §18, "The Tokens tool window", "A turn's card,
/// the popup and the info card"): the turn running, call by call; what the next message will cost, before the prompt cache
/// expires and after; and the tab's breakdown, its latest turns and findings. Every tab keeps its turn running, but the rows
/// and tiles are made, and the breakdown read from the usage history, only while something shows them (the Tokens window or
/// the composer's popup), and the breakdown again as the tab's turns are recorded.
/// </summary>
public sealed partial class TabTokensViewModel : ObservableObject
{
    /// <summary>How many of the latest turns the tool window lists, and the popup's chart shows.</summary>
    private const int Latest = 6;

    private const int ChartTurns = 24;

    private readonly AppServices _services;
    private readonly TabViewModel _tab;
    private readonly HashSet<string> _wantedBy = new(StringComparer.Ordinal);
    private TurnCalls _live = new();
    private TokenBreakdown? _breakdown;
    private LimitShare? _limit;
    private int _reads;

    internal TabTokensViewModel(AppServices services, TabViewModel tab)
    {
        _services = services;
        _tab = tab;
        // The indicator is read again after each turn: the context between turns, and the next message's cost, follow it.
        tab.Context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ContextViewModel.Percent))
            {
                RefreshNextMessage();
                if (IsWanted && !IsRunning)
                {
                    BuildContext(null);
                }
            }
        };
    }

    /// <summary>The tab, for what its views bind to beside this: the prompt cache's chip and the context.</summary>
    public TabViewModel Tab => _tab;

    // ---- The turn running ------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNextMessage), nameof(HasCalls))]
    public partial bool IsRunning { get; private set; }

    /// <summary>*Turn 15*.</summary>
    [ObservableProperty]
    public partial string RunningTitle { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<RunningCallRow> RunningCalls { get; private set; } = [];

    /// <summary>
    /// *86.1k tokens · $0.08 so far*, the turn's: its cost at the model's price from its earlier turns, left out before it
    /// has had one.
    /// </summary>
    [ObservableProperty]
    public partial string RunningSummary { get; private set; } = "";

    /// <summary>The last thing the turn did was a tool's result: Claude is choosing its next step.</summary>
    [ObservableProperty]
    public partial bool IsChoosing { get; private set; }

    /// <summary>How full the main agent's context is: *41.4k of 200k*; with the bar's fill and the auto-compact mark.</summary>
    [ObservableProperty]
    public partial string ContextText { get; private set; } = "";

    [ObservableProperty]
    public partial double ContextShare { get; private set; }

    /// <summary>The context bar: what's in the window, the space free, and the auto-compact buffer when it's known.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ContextSegment> ContextSegments { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasContext { get; private set; }

    /// <summary>The session's events, on the UI thread: the turn running follows its calls as they arrive.</summary>
    internal void OnSessionEvent(SessionEvent sessionEvent)
    {
        var at = _services.Time.GetUtcNow();
        switch (sessionEvent)
        {
            case TurnStarted:
                IsRunning = true;
                break;
            case AssistantMessageReceived assistant:
                IsRunning = true;
                _live.Add(assistant.Message, at);
                IsChoosing = false;
                break;
            case ToolResultsReceived results:
                _live.AddResults(results.Message, at);
                IsChoosing = results.Message.ParentToolUseId is null && results.Message.Content.OfType<ToolResultBlock>().Any();
                break;
            case SystemNotice { Message.CompactBoundary: { } compacted }:
                _live.Compacted(compacted, at);
                break;
            case ConversationReset:
                _live.Reset();
                break;
            case TurnCompleted:
                // Its calls are recorded with it, and come back with the breakdown.
                _live = new TurnCalls();
                IsRunning = false;
                IsChoosing = false;
                break;
            default:
                return;
        }
        if (IsWanted)
        {
            BuildRunning();
            BuildTiles();
        }
    }

    /// <summary>The session ended or was replaced: what was running isn't any more.</summary>
    internal void OnSessionEnded()
    {
        _live = new TurnCalls();
        IsRunning = false;
        IsChoosing = false;
        if (IsWanted)
        {
            BuildRunning();
            BuildTiles();
        }
    }

    private void BuildRunning()
    {
        var log = _live.SoFar();
        var calls = log.Calls;
        RunningTitle = string.Create(CultureInfo.CurrentCulture, $"Turn {_tab.State.Tokens.Turns + 1}");
        var main = calls.Where(c => c.Agent is null).ToList();
        var subagents = calls.Where(c => c.Agent is not null).GroupBy(c => c.Agent).ToList();
        var largest = main.Select(c => (double)c.Tokens.Total).Concat(subagents.Select(g => (double)g.Sum(c => c.Tokens.Total))).DefaultIfEmpty(0).Max();
        var steps = log.Steps.Where(s => s.Agent is null).ToLookup(s => s.CallSeq);
        var rows = new List<RunningCallRow>();
        foreach (var call in main)
        {
            rows.Add(new RunningCallRow($"c{call.Seq}", call.Kind == CallKind.CacheExpired ? "cache expired" : "", Kinds(call.Tokens),
                largest > 0 ? call.Tokens.Total / largest : 0, Formats.Tokens(call.Tokens.Total), Cost(call) is { } cost ? Formats.Dollars(cost) : "", IsStep: false));
            foreach (var step in steps[call.Seq])
            {
                rows.Add(new RunningCallRow("", $"↳ {step.Tool} · +{Formats.Tokens(step.Tokens)}", null, 0, "", "", IsStep: true));
            }
        }
        foreach (var agent in subagents)
        {
            var tokens = agent.Aggregate(CallTokens.Zero, (sum, c) => sum.Plus(c.Tokens));
            var cost = agent.Select(Cost).ToList();
            rows.Add(new RunningCallRow("◇", $"{agent.First().AgentType ?? "Subagent"}: {agent.Count()} call{(agent.Count() == 1 ? "" : "s")}", Kinds(tokens),
                largest > 0 ? tokens.Total / largest : 0, Formats.Tokens(tokens.Total), cost.All(c => c is not null) ? Formats.Dollars(cost.Sum(c => c!.Value)) : "", IsStep: false));
        }
        RunningCalls = rows;
        var total = calls.Sum(c => c.Tokens.Total);
        var costs = calls.Select(Cost).ToList();
        RunningSummary = calls.Count > 0 && costs.All(c => c is not null)
            ? $"{Formats.Tokens(total)} tokens · {Formats.Dollars(costs.Sum(c => c!.Value))} so far"
            : $"{Formats.Tokens(total)} tokens";
        BuildContext(main.LastOrDefault() is { } last ? last.Tokens.Total : null);
    }

    /// <summary>How full the main agent's context is: its last call's during a turn, the indicator's between turns.</summary>
    private void BuildContext(long? lastCall)
    {
        var tokens = lastCall ?? _tab.Context.Tokens;
        var window = _tab.Context.Window;
        HasContext = tokens is not null && window is > 0;
        if (tokens is { } t && window is { } w && w > 0)
        {
            var used = Math.Min(t, w);
            var compactAt = _tab.Context.AutoCompactAt is { } at && at > 0 && at < w ? at : (long?)null;
            ContextShare = (double)used / w;
            var segments = new List<ContextSegment> { new("In the context", used, ContextBreakdown.EstimateBrush, $"{Formats.Tokens(used)} tokens in the context") };
            if (compactAt is { } limit)
            {
                segments.Add(new("Free", Math.Max(0, limit - used), ContextBreakdown.FreeBrush, "Free"));
                segments.Add(new("Auto-compact buffer", Math.Min(w - limit, w - used), ContextBreakdown.BufferBrush,
                    $"Claude Code compacts the conversation by itself at {Formats.Tokens(limit)}"));
            }
            else
            {
                segments.Add(new("Free", w - used, ContextBreakdown.FreeBrush, "Free"));
            }
            ContextSegments = segments;
            ContextText = compactAt is { } compacts
                ? $"{Formats.Tokens(t)} of {Formats.Tokens(w)} · compacts at {Formats.Tokens(compacts)}"
                : $"{Formats.Tokens(t)} of {Formats.Tokens(w)}";
        }
    }

    // ---- The next message ------------------------------------------------------------------------------------------

    /// <summary>Between turns, while the prompt cache's chip shows and the model's price is known.</summary>
    public bool ShowsNextMessage => !IsRunning && HasNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNextMessage))]
    public partial bool HasNext { get; private set; }

    /// <summary>*Before 13:20*, until the cache expires.</summary>
    [ObservableProperty]
    public partial string NextBefore { get; private set; } = "";

    [ObservableProperty]
    public partial string NextWarmCost { get; private set; } = "";

    /// <summary>The first call read from the cache: its bar, all cache read, scaled to the call that writes it again.</summary>
    [ObservableProperty]
    public partial KindValues NextWarmValues { get; private set; } = KindValues.Zero;

    [ObservableProperty]
    public partial double NextWarmScale { get; private set; }

    /// <summary>*After it*, or *Now* once the cache has expired.</summary>
    [ObservableProperty]
    public partial string NextAfter { get; private set; } = "";

    [ObservableProperty]
    public partial string NextColdCost { get; private set; } = "";

    [ObservableProperty]
    public partial KindValues NextColdValues { get; private set; } = KindValues.Zero;

    [ObservableProperty]
    public partial bool IsCacheExpired { get; private set; }

    [ObservableProperty]
    public partial string NextNote { get; private set; } = "";

    /// <summary>
    /// What the next message's first call costs, from the context's size at the model's price (DESIGN.md §18, "Your next
    /// message"): read from the cache before it expires, written to it again after. Its own words and Claude's answer are
    /// left out: Claude decides how long a turn is.
    /// </summary>
    internal void RefreshNextMessage()
    {
        var context = _tab.Context.Tokens;
        var model = _tab.TokenModelId;
        var price = model is null ? null : PriceOf(model);
        if (_tab.TokenCacheExpiry is not { } expiry || context is not { } tokens || price is not { } input)
        {
            HasNext = false;
            return;
        }
        var warm = tokens * input * TokenPrices.Read;
        var cold = tokens * input * (expiry.Hour ? TokenPrices.Write1h : TokenPrices.Write5m);
        var expired = expiry.ExpiresAt <= _services.Time.GetUtcNow();
        var until = TimeZoneInfo.ConvertTime(expiry.ExpiresAt, _services.Time.LocalTimeZone).ToString("HH:mm", CultureInfo.CurrentCulture);
        IsCacheExpired = expired;
        NextWarmCost = Formats.Dollars(warm);
        NextColdCost = Formats.Dollars(cold);
        NextWarmValues = new KindValues(tokens, 0, 0, 0);
        NextColdValues = new KindValues(0, tokens, 0, 0);
        NextWarmScale = cold > 0 ? Math.Max(0.03, warm / cold) : 0;
        NextBefore = $"Before {until}";
        NextAfter = expired ? "Now" : "After it";
        HasNext = true;
        NextNote = expired
            ? $"The cache expired at {until}: your next message's first call writes this conversation's {Formats.Tokens(tokens)} tokens to it again."
            : $"Its first call reads this conversation's {Formats.Tokens(tokens)} tokens from the cache until {until}. After that, Claude writes all of it to the cache again.";
    }

    // ---- The tab ---------------------------------------------------------------------------------------------------

    /// <summary>The tab has made a call: the Tokens window has its icon.</summary>
    public bool HasCalls => IsRunning || _tab.State.Tokens.Turns > 0;

    /// <summary>The Tokens window's tiles: tokens, cost, the share of the session limit and the cache's.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TokenTile> Tiles { get; private set; } = [];

    [ObservableProperty]
    public partial KindValues TokenValues { get; private set; } = KindValues.Zero;

    [ObservableProperty]
    public partial KindValues CostValues { get; private set; } = KindValues.Zero;

    [ObservableProperty]
    public partial IReadOnlyList<LatestTurnRow> LatestTurns { get; private set; } = [];

    /// <summary>The latest turns for the popup's chart, by kind, with their marks.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TurnColumn> Columns { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TokenFindingRow> Findings { get; private set; } = [];

    /// <summary>*All 14 turns in the breakdown ›*.</summary>
    [ObservableProperty]
    public partial string AllTurnsText { get; private set; } = "";

    /// <summary>*Last turn: 162k · $0.14*, for the popup.</summary>
    [ObservableProperty]
    public partial string LastTurnText { get; private set; } = "";

    /// <summary>
    /// The info card's Tokens row (DESIGN.md §4, "Tab info card"): *10.1M tokens · about $10.47 · ≈ 23% of the session
    /// limit*, the share once it has been read.
    /// </summary>
    public string InfoText
    {
        get
        {
            var totals = _tab.State.Tokens;
            var text = $"{_tab.Context.TokensShort} · about {Formats.Dollars(totals.EstimatedCostUsd)} · {TabBurnRow.TurnsText(totals.Turns)}";
            return _limit is { Points: > 0 } limit && _tab.Account is null
                ? text + string.Create(CultureInfo.CurrentCulture, $" · ≈ {limit.Points:0}% of the session limit")
                : text;
        }
    }

    /// <summary>
    /// Something shows the tab's breakdown, or stopped showing it: the Tokens window (<c>window</c>) or the composer's popup
    /// (<c>popup</c>). The breakdown is read as the first comes, and kept up while any shows.
    /// </summary>
    internal void SetWanted(string by, bool wanted)
    {
        var was = _wantedBy.Count > 0;
        if (wanted)
        {
            _wantedBy.Add(by);
        }
        else
        {
            _wantedBy.Remove(by);
        }
        if (!was && _wantedBy.Count > 0)
        {
            // What the views show was left as it was while none did.
            BuildRunning();
            BuildTiles();
            BuildTurns();
            Refresh();
        }
    }

    internal bool IsWanted => _wantedBy.Count > 0;

    /// <summary>One of the tab's turns is in the usage history: read the breakdown again, while something shows it.</summary>
    internal void OnTurnRecorded()
    {
        OnPropertyChanged(nameof(HasCalls));
        if (IsWanted)
        {
            Refresh();
        }
    }

    /// <summary>The latest read of the breakdown, for tests to wait on.</summary>
    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>Reads the tab's breakdown, all of its turns, and its share of the session limit; the latest read wins.</summary>
    internal void Refresh() => Loaded = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_services.Usage is not { } usage)
        {
            return;
        }
        var read = ++_reads;
        var now = _services.Time.GetUtcNow();
        var tabId = _tab.Id;
        var plan = _tab.Account is null;
        try
        {
            var (breakdown, limit) = await Task.Run(() =>
                (TokenBreakdown.Read(usage.Store, tabId, DateTimeOffset.MinValue, now), plan ? LimitShare.Read(usage.Store, tabId, now) : null)).ConfigureAwait(false);
            _services.Dispatcher.Post(() =>
            {
                if (read != _reads)
                {
                    return;
                }
                _breakdown = breakdown;
                _limit = limit;
                BuildTiles();
                BuildTurns();
                RefreshNextMessage();
                OnPropertyChanged(nameof(InfoText));
                _tab.TokensInfoChanged();
            });
        }
        catch (Exception)
        {
            // The usage history is optional here.
        }
    }

    /// <summary>The tab's tiles and bars, from its breakdown and the turn running.</summary>
    private void BuildTiles()
    {
        var live = _live.SoFar().Calls;
        var liveTokens = live.Aggregate(CallTokens.Zero, (sum, c) => sum.Plus(c.Tokens));
        var liveCost = live.Select(Cost).Sum(c => c ?? 0);
        var totals = _tab.State.Tokens;
        var kinds = (_breakdown?.Tokens ?? TokenKinds.Zero).Plus(new TokenKinds(liveTokens.CacheRead, liveTokens.CacheWrite, liveTokens.Input, liveTokens.Output));
        var costs = _breakdown?.Costs ?? KindCosts.Zero;
        TokenValues = KindValues.Of(kinds);
        CostValues = KindValues.Of(costs);
        Tiles =
        [
            new("Tokens", Formats.Tokens(totals.Total + liveTokens.Total), IsRunning ? "with the turn so far" : "this tab's"),
            new("Cost", Formats.Dollars(totals.EstimatedCostUsd + liveCost), "estimated"),
            _tab.Account is not null ? new("Session limit", "–", "an API key's")
                : _limit is { } limit ? new("Session limit", string.Create(CultureInfo.CurrentCulture, $"≈ {limit.Points:0}%"), "this window", IsEstimate: true)
                : new("Session limit", "–", "not read yet", IsEstimate: true),
            new("From the cache", kinds.CacheShare is { } share ? string.Create(CultureInfo.CurrentCulture, $"{share * 100:0}%") : "–", "of what Claude read"),
        ];
    }

    /// <summary>The tab's latest turns, its chart and findings, from its breakdown as it was last read.</summary>
    private void BuildTurns()
    {
        if (_breakdown is not { } b)
        {
            return;
        }
        var prompts = TokenBreakdownViewModel.PromptsOf(_tab.Items, b.Turns);
        var recent = b.Turns.TakeLast(Latest).Reverse().ToList();
        var largest = recent.Select(t => t.CostUsd).DefaultIfEmpty(0).Max();
        LatestTurns = [.. recent.Select(t => new LatestTurnRow(t.Number, t.Turn, prompts.GetValueOrDefault(t) ?? $"Turn {t.Number}", MarkOf(t), KindValues.Of(t.Costs),
            largest > 0 ? t.CostUsd / largest : 0, Formats.Dollars(t.CostUsd), IsRunning: false))];
        Columns = [.. b.Turns.TakeLast(ChartTurns).Select(t => new TurnColumn(t.Number, "", KindValues.Of(t.Tokens), MarkOf(t), null,
            string.Create(CultureInfo.CurrentCulture, $"Turn {t.Number}: {Formats.Tokens(t.Tokens.Total)} tokens · {Formats.Dollars(t.CostUsd)}")))];
        Findings = [.. b.Findings.Take(2).Select(f => new TokenFindingRow(f.Kind, f.Describe(_tab.ModelNameFor), f.Turn, ShowTurnCommand))];
        AllTurnsText = string.Create(CultureInfo.CurrentCulture, $"All {b.Turns.Count} turn{(b.Turns.Count == 1 ? "" : "s")} in the breakdown ›");
        LastTurnText = b.Turns.LastOrDefault() is { } lastTurn
            ? string.Create(CultureInfo.CurrentCulture, $"Last turn: {Formats.Tokens(lastTurn.Tokens.Total)} · {Formats.Dollars(lastTurn.CostUsd)}")
            : "";
    }

    private static TurnMark MarkOf(TurnBreakdown turn) =>
        turn.CacheExpired ? TurnMark.CacheExpired : turn.Compacted ? TurnMark.Compacted : turn.SubagentCalls > 0 ? TurnMark.Subagent : TurnMark.None;

    private static KindValues Kinds(CallTokens tokens) => new(tokens.CacheRead, tokens.CacheWrite, tokens.Input, tokens.Output);

    /// <summary>A call of the turn running at its model's price, from the tab's earlier turns; null before it has had one.</summary>
    private double? Cost(ModelCall call) => PriceOf(call.Model) is { } input
        ? input * (call.Tokens.Input + (call.Tokens.CacheRead * TokenPrices.Read) + (call.Tokens.Write5m * TokenPrices.Write5m)
            + (call.Tokens.Write1h * TokenPrices.Write1h) + (call.Tokens.Output * TokenPrices.Output))
        : null;

    /// <summary>
    /// A model's price for fresh input: from the tab's breakdown when it has been read, or else from the tab's own totals,
    /// whose writes weigh as five minutes'.
    /// </summary>
    private double? PriceOf(string model)
    {
        if (_breakdown?.Prices.Input(model) is { } price)
        {
            return price;
        }
        if (_tab.State.Tokens.Models.TryGetValue(model, out var totals))
        {
            var weight = totals.Input + (totals.CacheRead * TokenPrices.Read) + (totals.CacheWrite * TokenPrices.Write5m) + (totals.Output * TokenPrices.Output);
            return weight > 0 && totals.EstimatedCostUsd > 0 ? totals.EstimatedCostUsd / weight : null;
        }
        return null;
    }

    /// <summary>A turn's row, a finding's link or the popup's chart: the tab's breakdown, on that turn.</summary>
    [RelayCommand]
    private void ShowTurn(int number) => _tab.ShowTokenBreakdownOn(null, number);

    /// <summary><b>Open breakdown</b>.</summary>
    [RelayCommand]
    private void OpenBreakdown() => _tab.ShowTokenBreakdownOn(null, null);

    /// <summary>A latest turn's row.</summary>
    [RelayCommand]
    private void OpenTurn(LatestTurnRow? row)
    {
        if (row is not null && !row.IsRunning)
        {
            _tab.ShowTokenBreakdownOn(row.TurnId, row.Number);
        }
    }
}
