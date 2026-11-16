using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deventry.App.Controls;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Files;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.ViewModels;

/// <summary>The periods the token breakdown's window shows (DESIGN.md §18, "The window").</summary>
public enum TokenPeriod
{
    SessionWindow,
    Today,
    AllTime,
}

/// <summary>A choice of <see cref="TokenPeriod"/>, shown by its label.</summary>
public sealed record TokenPeriodChoice(TokenPeriod Period)
{
    public override string ToString() => Period switch
    {
        TokenPeriod.SessionWindow => "This session window",
        TokenPeriod.Today => "Today",
        _ => "All time",
    };
}

/// <summary>A summary tile at the top of the window.</summary>
/// <param name="IsEstimate">Marked as an estimate, as the share of the session limit is.</param>
public sealed record TokenTile(string Label, string Value, string Detail, bool IsEstimate = false, string? Tip = null);

/// <summary>A kind of token in the composition's legend: its color, what it is, and its share of the tokens and of the cost.</summary>
public sealed record TokenKindRow(string Name, KindValues Swatch, string About, string Tokens, string TokenShare, string Cost, string CostShare);

/// <summary>A finding, as the window lists it, with the way to its turn.</summary>
public sealed record TokenFindingRow(TokenFindingKind Kind, string Text, int Turn, IRelayCommand GoToTurn)
{
    public bool IsCaution => Kind == TokenFindingKind.CacheExpired;

    public string GoToText => string.Create(CultureInfo.CurrentCulture, $"Go to turn {Turn} ›");
}

/// <summary>A row of the window's by-model or by-agent tables.</summary>
public sealed record TokenShareRow(string Name, string? Detail, string Calls, KindValues Values, double Scale, string Tokens, string Cost);

/// <summary>A row of the window's by-tool table: what its results added, and what later calls read again because of them.</summary>
public sealed record TokenToolRow(string Tool, string Calls, string Added, double Scale, string ReadAgain, string Cost);

/// <summary>A model call, or a tool result between calls, under a turn opened in the window's table.</summary>
public sealed record TokenCallRow(string Label, string What, string Agent, KindValues? Values, double Scale, string Tokens, string Cost, string Cache, string Context, bool IsStep, bool IsCaution);

/// <summary>A turn in the window's table (DESIGN.md §18, "The window"), which opens to its calls.</summary>
public sealed partial class TokenTurnRow(TurnBreakdown turn, string prompt, string when, IReadOnlyList<string> chips, KindValues values, double scale, string context,
    Func<IReadOnlyList<TokenCallRow>> calls) : ObservableObject
{
    public TurnBreakdown Turn { get; } = turn;

    public int Number => Turn.Number;

    public string NumberText => Turn.Number.ToString(CultureInfo.CurrentCulture);

    /// <summary>Its prompt's first line, from the conversation; *Turn 3* when it isn't there.</summary>
    public string Prompt { get; } = prompt;

    /// <summary>When it ended and how long it ran: *11:32 · 2m 05s*.</summary>
    public string When { get; } = when;

    /// <summary>Its findings, briefly: *Cache expired · 1h 12m idle*, *Compacted first*, *Explore · 1.6M*.</summary>
    public IReadOnlyList<string> Chips { get; } = chips;

    public bool CacheExpired => Turn.CacheExpired;

    public string CallsText => Turn.HasCalls
        ? Turn.SubagentCalls > 0 ? string.Create(CultureInfo.CurrentCulture, $"{Turn.MainCalls} +{Turn.SubagentCalls}") : Turn.MainCalls.ToString(CultureInfo.CurrentCulture)
        : "–";

    public KindValues Values { get; } = values;

    public double Scale { get; } = scale;

    public string Tokens => Formats.Tokens(Turn.Tokens.Total);

    public string Cost => Formats.Dollars(Turn.CostUsd);

    public string Cache => Turn.Tokens.CacheShare is { } share ? string.Create(CultureInfo.CurrentCulture, $"{share * 100:0}%") : "–";

    public string Context { get; } = context;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Calls))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Its calls in order, with the tool results between them, once it's opened.</summary>
    public IReadOnlyList<TokenCallRow> Calls => IsExpanded ? _calls ??= calls() : [];

    public bool HasCalls => Turn.HasCalls;

    private IReadOnlyList<TokenCallRow>? _calls;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>
/// A tab's token breakdown in a window of its own (DESIGN.md §18, "The window"): its tiles, what its tokens were, its turns
/// as a chart and a table that opens to each turn's calls, the findings, and its tokens by model, agent and tool, for a
/// period, in tokens or cost. Read from the usage history off the UI thread, and again as the tab's turns are recorded. An
/// open tab's, or one the Usage panel lists that's closed or another machine's (<see cref="ITokenBreakdownSubject"/>).
/// </summary>
public sealed partial class TokenBreakdownViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ITokenBreakdownSubject _tab;
    private TokenBreakdown? _breakdown;
    private LimitShare? _limit;
    private int _reads;

    internal TokenBreakdownViewModel(AppServices services, TabViewModel tab)
        : this(services, new OpenTabBreakdown(tab))
    {
    }

    internal TokenBreakdownViewModel(AppServices services, ITokenBreakdownSubject subject)
    {
        _services = services;
        _tab = subject;
        ShowCost = services.State.TokensAsCost;
        if (services.Usage is { } usage)
        {
            usage.TurnRecorded += OnTurnRecorded;
        }
    }

    public string TabId => _tab.Id;

    public string Title => $"Token breakdown · {_tab.Name}";

    public IReadOnlyList<TokenPeriodChoice> Periods { get; } = [.. Enum.GetValues<TokenPeriod>().Select(p => new TokenPeriodChoice(p))];

    [ObservableProperty]
    public partial TokenPeriodChoice SelectedPeriod { get; set; } = new(TokenPeriod.SessionWindow);

    partial void OnSelectedPeriodChanged(TokenPeriodChoice value) => Refresh();

    /// <summary><b>Tokens | Cost</b>: the chart and bars show cost. Remembered on this machine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTokens), nameof(ChartUnit), nameof(BarsHeader))]
    public partial bool ShowCost { get; set; }

    public bool ShowTokens
    {
        get => !ShowCost;
        set => ShowCost = !value;
    }

    partial void OnShowCostChanged(bool value)
    {
        if (_services.State.TokensAsCost != value)
        {
            _services.State.TokensAsCost = value;
            _services.SaveState();
        }
        Build();
    }

    public string ChartUnit => ShowCost ? "Estimated cost per turn" : "Tokens per turn";

    public string BarsHeader => ShowCost ? "Cost by kind" : "Tokens by kind";

    /// <summary>The project, branch, session and turns: *Starfall · fix/grapple · session 6f1c2a9e · 14 turns since 09:12*.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<TokenTile> Tiles { get; set; } = [];

    [ObservableProperty]
    public partial KindValues TokenValues { get; set; } = KindValues.Zero;

    [ObservableProperty]
    public partial KindValues CostValues { get; set; } = KindValues.Zero;

    [ObservableProperty]
    public partial IReadOnlyList<TokenKindRow> Kinds { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TurnColumn> Columns { get; set; } = [];

    [ObservableProperty]
    public partial long? ContextWindow { get; set; }

    public ObservableCollection<TokenTurnRow> Turns { get; } = [];

    [ObservableProperty]
    public partial int SelectedTurn { get; set; }

    /// <summary>A turn was asked for, from the chart, a finding or elsewhere: the view scrolls its row into view.</summary>
    public event Action<TokenTurnRow>? TurnShown;

    [ObservableProperty]
    public partial IReadOnlyList<TokenFindingRow> Findings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TokenShareRow> Models { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TokenShareRow> Agents { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TokenToolRow> Tools { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasTurns { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    public bool IsEmpty => IsLoaded && !HasTurns;

    public bool HasFindings => Findings.Count > 0;

    partial void OnFindingsChanged(IReadOnlyList<TokenFindingRow> value) => OnPropertyChanged(nameof(HasFindings));

    public string Footnote =>
        "Costs are Claude Code's estimates at list price, not your bill; on a plan, its limits are what count. The share of the session limit is an estimate: "
        + "how far the session meter rose while this tab worked, split between the tabs working then by their cost. Each model call is counted once, subagents "
        + "included. The usage history keeps only numbers and tools' names: the prompts are read from the conversation.";

    /// <summary>The latest read, for tests to wait on.</summary>
    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>Reads the tab's breakdown again; the latest read wins.</summary>
    public void Refresh() => Loaded = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_services.Usage is not { } usage)
        {
            IsLoaded = true;
            return;
        }
        var read = ++_reads;
        var now = _services.Time.GetUtcNow();
        var from = From(SelectedPeriod.Period, now, usage.Current);
        var tabId = _tab.Id;
        var planTab = _tab.UsesPlan;
        try
        {
            var (breakdown, limit) = await Task.Run(() =>
                (TokenBreakdown.Read(usage.Store, tabId, from, now), planTab ? LimitShare.Read(usage.Store, tabId, now) : null)).ConfigureAwait(false);
            _services.Dispatcher.Post(() =>
            {
                // A read a later one has overtaken drops its result.
                if (read != _reads)
                {
                    return;
                }
                _breakdown = breakdown;
                _limit = limit;
                Build();
                IsLoaded = true;
            });
        }
        catch (Exception)
        {
            // The usage history is optional here.
            _services.Dispatcher.Post(() => IsLoaded = true);
        }
    }

    /// <summary>Where a period starts: the current 5-hour window's start, local midnight, or the beginning.</summary>
    private DateTimeOffset From(TokenPeriod period, DateTimeOffset now, UsageSnapshot? current) => period switch
    {
        TokenPeriod.SessionWindow => current?.Session?.ResetsAt is { } resets && resets > now ? resets - LimitShare.Window : now - LimitShare.Window,
        TokenPeriod.Today => new DateTimeOffset(TimeZoneInfo.ConvertTime(now, _services.Time.LocalTimeZone).Date, TimeZoneInfo.ConvertTime(now, _services.Time.LocalTimeZone).Offset),
        _ => DateTimeOffset.MinValue,
    };

    private void OnTurnRecorded(string tabId)
    {
        if (tabId == _tab.Id)
        {
            Refresh();
        }
    }

    /// <summary>A turn asked for before the breakdown was read: shown once it is.</summary>
    private (string? TurnId, int? Number)? _asked;

    /// <summary>
    /// Shows a turn by its id (a turn's card) or its number (the Tokens window), now or once the breakdown has been read.
    /// Neither leaves the window as it is.
    /// </summary>
    public void Show(string? turnId, int? number)
    {
        if (turnId is null && number is null)
        {
            return;
        }
        _asked = (turnId, number);
        ShowAsked();
    }

    private void ShowAsked()
    {
        if (_asked is not { } asked || Turns.Count == 0)
        {
            return;
        }
        if (Turns.FirstOrDefault(r => asked.TurnId is not null ? r.Turn.Turn == asked.TurnId : r.Number == asked.Number) is { } row)
        {
            _asked = null;
            ShowTurn(row.Number);
        }
    }

    /// <summary>Shows a turn: selects its row, opens it, and has the view bring it into view.</summary>
    [RelayCommand]
    private void ShowTurn(int number)
    {
        SelectedTurn = number;
        foreach (var row in Turns)
        {
            row.IsSelected = row.Number == number;
            if (row.Number == number)
            {
                row.IsExpanded = true;
                TurnShown?.Invoke(row);
            }
        }
    }

    /// <summary>Lays the breakdown out for the view, in tokens or cost.</summary>
    private void Build()
    {
        if (_breakdown is not { } b)
        {
            return;
        }
        var cost = ShowCost;
        HasTurns = b.Turns.Count > 0;
        var name = ModelName;

        var first = b.Turns.FirstOrDefault();
        var parts = new List<string>();
        if (_tab.Project is { } project)
        {
            parts.Add(project);
        }
        if (_tab.Branch is { } branch)
        {
            parts.Add(branch);
        }
        if (_tab.SessionId is { Length: >= 8 } session)
        {
            parts.Add($"session {session[..8]}");
        }
        if (first is not null)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{b.Turns.Count} turn{(b.Turns.Count == 1 ? "" : "s")} since {Local(StartOf(first)):HH:mm}"));
        }
        Line = string.Join(" · ", parts);

        var calls = b.Turns.SelectMany(t => t.Calls).ToList();
        var mainCalls = b.Turns.Sum(t => t.MainCalls);
        var subagentCalls = b.Turns.Sum(t => t.SubagentCalls);
        var subagents = calls.Where(c => c.Agent is not null).Select(c => c.Agent).Distinct().Count();
        Tiles =
        [
            new("Tokens", Formats.Tokens(b.Tokens.Total), b.Tokens.Total.ToString("N0", CultureInfo.CurrentCulture)),
            new("Estimated cost", Formats.Dollars(b.CostUsd), "at list price, not your bill"),
            LimitTile(),
            new("From the cache", b.Tokens.CacheShare is { } share ? Percent(share) : "–", $"{Formats.Tokens(b.Tokens.CacheRead)} read, {Formats.Tokens(b.Tokens.CacheWrite)} written",
                Tip: "The share of what Claude read that came from the prompt cache."),
            new("Model calls", b.CallCount.ToString("N0", CultureInfo.CurrentCulture),
                subagentCalls > 0 ? string.Create(CultureInfo.CurrentCulture, $"{mainCalls} main · {subagentCalls} by {subagents} subagent{(subagents == 1 ? "" : "s")}") : string.Create(CultureInfo.CurrentCulture, $"{mainCalls} main")),
            new("Tool calls", b.Turns.Sum(t => t.ToolCalls).ToString("N0", CultureInfo.CurrentCulture),
                string.Create(CultureInfo.CurrentCulture, $"in {b.Turns.Count} turn{(b.Turns.Count == 1 ? "" : "s")}")),
        ];

        TokenValues = KindValues.Of(b.Tokens);
        CostValues = KindValues.Of(b.Costs);
        Kinds = KindRows(b.Tokens, b.Costs);

        var prompts = PromptsOf(b.Turns);
        Columns = [.. b.Turns.Select(t => new TurnColumn(t.Number, Local(t.At).ToString("HH:mm", CultureInfo.CurrentCulture), Values(t.Tokens, t.Costs), MarkOf(t), t.ContextAfter,
            ColumnTip(t, prompts.GetValueOrDefault(t)))) ];
        ContextWindow = ContextWindowOf(b.Turns);

        var largest = b.Turns.Count > 0 ? b.Turns.Max(t => cost ? t.CostUsd : t.Tokens.Total) : 0;
        var expanded = Turns.Where(r => r.IsExpanded).Select(r => r.Number).ToHashSet();
        Turns.Clear();
        foreach (var turn in b.Turns)
        {
            var row = new TokenTurnRow(turn, prompts.GetValueOrDefault(turn) ?? $"Turn {turn.Number}", WhenOf(turn), ChipsOf(turn), Values(turn.Tokens, turn.Costs),
                largest > 0 ? (cost ? turn.CostUsd : turn.Tokens.Total) / largest : 0, turn.ContextAfter is { } context && ContextWindow is { } window
                    ? Percent((double)context / window) : "–", () => CallRowsOf(b, turn))
            {
                IsExpanded = expanded.Contains(turn.Number),
                IsSelected = turn.Number == SelectedTurn,
            };
            Turns.Add(row);
        }

        Findings = [.. b.Findings.Select(f => new TokenFindingRow(f.Kind, f.Describe(name), f.Turn, ShowTurnCommand))];
        ShowAsked();
        var largestModel = b.ByModel.Count > 0 ? b.ByModel.Max(m => cost ? m.CostUsd : m.Tokens.Total) : 0;
        Models = [.. b.ByModel.Select(m => new TokenShareRow(name(m.Model), null, m.Calls > 0 ? m.Calls.ToString("N0", CultureInfo.CurrentCulture) : "–",
            Values(m.Tokens, m.Costs), Scaled(cost ? m.CostUsd : m.Tokens.Total, largestModel), Formats.Tokens(m.Tokens.Total), Formats.Dollars(m.CostUsd)))];
        var largestAgent = b.ByAgent.Count > 0 ? b.ByAgent.Max(a => cost ? a.CostUsd : a.Tokens.Total) : 0;
        Agents = [.. b.ByAgent.Select(a => new TokenShareRow(AgentName(a), AgentDetail(a), a.Calls > 0 ? a.Calls.ToString("N0", CultureInfo.CurrentCulture) : "–",
            Values(a.Tokens, a.Costs), Scaled(cost ? a.CostUsd : a.Tokens.Total, largestAgent), Formats.Tokens(a.Tokens.Total), Formats.Dollars(a.CostUsd)))];
        var largestTool = b.ByTool.Count > 0 ? b.ByTool.Max(t => t.ReadAgain) : 0;
        Tools = [.. b.ByTool.Select(t => new TokenToolRow(t.Tool, t.Calls.ToString("N0", CultureInfo.CurrentCulture), Formats.Tokens(t.Added),
            Scaled(t.ReadAgain, largestTool), Formats.Tokens(t.ReadAgain), Formats.Dollars(t.ReadAgainCostUsd)))];

        KindValues Values(TokenKinds tokens, KindCosts costs) => cost ? KindValues.Of(costs) : KindValues.Of(tokens);

    }

    /// <summary>
    /// What each kind of token was, its tokens and cost and their shares (DESIGN.md §18, "Kinds of token"): the window's
    /// composition, and the Usage panel's <b>Where the tokens went</b>.
    /// </summary>
    internal static IReadOnlyList<TokenKindRow> KindRows(TokenKinds tokens, KindCosts costs) =>
    [
        Kind("Cache read", new(1, 0, 0, 0), "Read from the prompt cache: the conversation so far, at a tenth of the input price", tokens.CacheRead, costs.CacheRead, tokens, costs),
        Kind("Cache write", new(0, 1, 0, 0), "Written to the prompt cache: what was new since the last call, at more than the input price", tokens.CacheWrite, costs.CacheWrite, tokens, costs),
        Kind("Fresh input", new(0, 0, 1, 0), "Neither read from the cache nor written to it. Claude Code caches nearly everything, so it's small", tokens.Input, costs.Input, tokens, costs),
        Kind("Output", new(0, 0, 0, 1), "What Claude wrote, thinking included, at five times the input price", tokens.Output, costs.Output, tokens, costs),
    ];

    private static TokenKindRow Kind(string kind, KindValues swatch, string about, long count, double dollars, TokenKinds tokens, KindCosts costs) => new(kind, swatch, about,
        count.ToString("N0", CultureInfo.CurrentCulture), tokens.Total > 0 ? Percent((double)count / tokens.Total, 1) : "–", Dollars(dollars),
        costs.Total > 0 ? Percent(dollars / costs.Total) : "–");

    /// <summary>A cost, with less than a cent said so rather than as $0.00.</summary>
    internal static string Dollars(double dollars) => dollars is > 0 and < 0.01 ? "<$0.01" : Formats.Dollars(dollars);

    private TokenTile LimitTile()
    {
        if (!_tab.UsesPlan)
        {
            return new("Session limit", "–", "an API key doesn't use the plan");
        }
        if (_limit is not { } limit)
        {
            return new("Session limit", "–", "no reading of this window yet", IsEstimate: true);
        }
        var tip = string.Create(CultureInfo.CurrentCulture,
            $"The meter rose {limit.Rise:0} points this window: {limit.Points:0} while this tab worked, shared by cost with the tabs working then, and {limit.Elsewhere:0} with no tab here working.");
        return new("Session limit", string.Create(CultureInfo.CurrentCulture, $"≈ {limit.Points:0}%"), "of this 5-hour window", IsEstimate: true, Tip: tip);
    }

    /// <summary>A turn's calls in order, with the tool results each one got, and a subagent's as one row.</summary>
    private List<TokenCallRow> CallRowsOf(TokenBreakdown b, TurnBreakdown turn)
    {
        var rows = new List<TokenCallRow>();
        var cost = ShowCost;
        var main = turn.Calls.Where(c => c.Agent is null).ToList();
        var subagents = turn.Calls.Where(c => c.Agent is not null).GroupBy(c => c.Agent).ToList();
        // Each row against the largest: a call, or a subagent's calls together.
        var largest = main.Select(c => cost ? c.CostUsd : c.Total)
            .Concat(subagents.Select(g => cost ? g.Sum(c => c.CostUsd) : g.Sum(c => (double)c.Total))).DefaultIfEmpty(0).Max();
        var last = main.LastOrDefault(c => c.Kind is CallKind.Call or CallKind.CacheExpired);
        foreach (var call in main)
        {
            var tokens = TokenKinds.Of(call);
            var what = call.Kind switch
            {
                CallKind.CacheExpired => $"The cache had expired: wrote all {Formats.Tokens(call.CacheWrite)} again",
                CallKind.Compaction => "Compacting: read the conversation, wrote its summary",
                CallKind.ClaudeCode => "Claude Code's own calls, which it doesn't show",
                _ when ReferenceEquals(call, last) => "Claude's answer",
                _ => "Claude chose its next step",
            };
            rows.Add(new TokenCallRow(call.Kind is CallKind.Call or CallKind.CacheExpired ? $"c{call.Seq}" : "", what, ModelName(call.Model),
                cost ? KindValues.Of(KindCosts.Of(call)) : KindValues.Of(tokens), Scaled(cost ? call.CostUsd : call.Total, largest), Formats.Tokens(call.Total),
                Formats.Dollars(call.CostUsd), tokens.CacheShare is { } share ? Percent(share) : "–",
                call.Kind is CallKind.Call or CallKind.CacheExpired && ContextWindow is { } window ? Percent((double)tokens.Context / window) : "", IsStep: false,
                IsCaution: call.Kind == CallKind.CacheExpired));
            foreach (var step in turn.Steps.Where(s => s.CallSeq == call.Seq && s.Agent is null))
            {
                var (again, _) = b.ReadAgainOf(step);
                var read = again switch
                {
                    0 => "read again by no later call",
                    1 => "read again by 1 later call",
                    _ => string.Create(CultureInfo.CurrentCulture, $"read again by {again} later calls"),
                };
                rows.Add(new TokenCallRow("", $"↳ {step.Tool} · its result added {Formats.Tokens(step.Tokens)}, {read}{(step.IsError ? " · failed" : "")}", "", null, 0,
                    "+" + Formats.Tokens(step.Tokens), "", "", "", IsStep: true, IsCaution: false));
            }
        }
        foreach (var agent in subagents)
        {
            var tokens = agent.Aggregate(TokenKinds.Zero, (sum, c) => sum.Plus(TokenKinds.Of(c)));
            var costs = agent.Aggregate(KindCosts.Zero, (sum, c) => sum.Plus(KindCosts.Of(c)));
            var type = agent.First().AgentType is { Length: > 0 } t ? $"{t} subagent" : "A subagent";
            rows.Add(new TokenCallRow("", string.Create(CultureInfo.CurrentCulture, $"◇ {type}: {agent.Count()} call{(agent.Count() == 1 ? "" : "s")}"),
                string.Join(", ", agent.Select(c => c.Model).Distinct().Select(ModelName)), cost ? KindValues.Of(costs) : KindValues.Of(tokens),
                Scaled(cost ? costs.Total : tokens.Total, largest), Formats.Tokens(tokens.Total), Formats.Dollars(costs.Total),
                tokens.CacheShare is { } share ? Percent(share) : "–", "", IsStep: false, IsCaution: false));
        }
        return rows;
    }

    /// <summary>
    /// Each turn's prompt, from the conversation: the user's last message sent before the turn's first call, or before it
    /// ended without calls, each message one turn's. A turn without a message of its own (from before the conversation the
    /// tab has now, or one Claude went on with) has none.
    /// </summary>
    private Dictionary<TurnBreakdown, string> PromptsOf(IReadOnlyList<TurnBreakdown> turns) => PromptsOf(_tab.Conversation, turns);

    /// <summary>Each turn's prompt from a conversation's items, as <see cref="PromptsOf(IReadOnlyList{TurnBreakdown})"/> says.</summary>
    internal static Dictionary<TurnBreakdown, string> PromptsOf(IEnumerable<ConversationItem> items, IReadOnlyList<TurnBreakdown> turns)
    {
        var messages = items.OfType<UserMessageItem>().Where(m => m.SentAt is not null).OrderBy(m => m.SentAt).ToList();
        var prompts = new Dictionary<TurnBreakdown, string>();
        var used = new HashSet<UserMessageItem>();
        foreach (var turn in turns.OrderBy(StartOf))
        {
            var start = StartOf(turn);
            if (messages.LastOrDefault(m => m.SentAt <= start) is { } message && used.Add(message)
                && message.Text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() is { } line)
            {
                prompts[turn] = line;
            }
        }
        return prompts;
    }

    /// <summary>When a turn started: its first call, or when it ended without calls.</summary>
    private static DateTimeOffset StartOf(TurnBreakdown turn) => turn.Calls.Count > 0 ? turn.Calls.Min(c => c.Timestamp) : turn.At;

    private string WhenOf(TurnBreakdown turn)
    {
        var at = Local(turn.At).ToString("HH:mm", CultureInfo.CurrentCulture);
        return turn.HasCalls ? $"{at} · {Formats.Elapsed(turn.At - StartOf(turn))}" : at;
    }

    private static List<string> ChipsOf(TurnBreakdown turn)
    {
        var chips = new List<string>();
        if (turn.CacheExpired)
        {
            chips.Add(turn.IdleBefore is { } idle ? $"Cache expired · {Formats.Duration(idle)} idle" : "Cache expired");
        }
        if (turn.Compacted)
        {
            chips.Add("Compacted first");
        }
        foreach (var agent in turn.Calls.Where(c => c.Agent is not null).GroupBy(c => c.Agent))
        {
            var tokens = agent.Sum(c => c.Total);
            chips.Add($"{(agent.First().AgentType is { Length: > 0 } type ? type : "Subagent")} · {Formats.Tokens(tokens)}");
        }
        return chips;
    }

    private static TurnMark MarkOf(TurnBreakdown turn) =>
        turn.CacheExpired ? TurnMark.CacheExpired : turn.Compacted ? TurnMark.Compacted : turn.SubagentCalls > 0 ? TurnMark.Subagent : TurnMark.None;

    private string ColumnTip(TurnBreakdown turn, string? prompt)
    {
        var tip = new StringBuilder();
        tip.Append(CultureInfo.CurrentCulture, $"Turn {turn.Number} · {Local(turn.At):HH:mm}");
        if (prompt is not null)
        {
            tip.Append('\n').Append(prompt);
        }
        tip.Append('\n').Append(CultureInfo.CurrentCulture, $"Cache read {turn.Tokens.CacheRead:N0} · {Formats.Dollars(turn.Costs.CacheRead)}");
        tip.Append('\n').Append(CultureInfo.CurrentCulture, $"Cache write {turn.Tokens.CacheWrite:N0} · {Formats.Dollars(turn.Costs.CacheWrite)}");
        tip.Append('\n').Append(CultureInfo.CurrentCulture, $"Fresh input {turn.Tokens.Input:N0} · {Formats.Dollars(turn.Costs.Input)}");
        tip.Append('\n').Append(CultureInfo.CurrentCulture, $"Output {turn.Tokens.Output:N0} · {Formats.Dollars(turn.Costs.Output)}");
        tip.Append('\n').Append(CultureInfo.CurrentCulture, $"{Formats.Tokens(turn.Tokens.Total)} tokens · {Formats.Dollars(turn.CostUsd)}");
        foreach (var chip in ChipsOf(turn))
        {
            tip.Append('\n').Append(chip);
        }
        return tip.ToString();
    }

    /// <summary>The smallest context window a Claude model has, for the context line when the tab hasn't said its own.</summary>
    private const long SmallestWindow = 200_000;

    /// <summary>
    /// The context window's size for the context line: what the tab's context indicator says it is, or failing that the
    /// smallest a Claude model has, or the largest context its turns reached if that's more.
    /// </summary>
    private long? ContextWindowOf(IReadOnlyList<TurnBreakdown> turns)
    {
        if (_tab.ContextWindow is { } window)
        {
            return window;
        }
        return turns.Select(t => t.ContextAfter).Max() is { } largest ? Math.Max(SmallestWindow, largest) : null;
    }

    private static string AgentName(AgentBreakdown agent) => agent.Kind switch
    {
        AgentKind.Main => "Main conversation",
        AgentKind.ClaudeCode => "Claude Code's own",
        _ => string.Create(CultureInfo.CurrentCulture, $"{(agent.Type is { Length: > 0 } type ? type : "Subagent")} · turn {agent.Turn}"),
    };

    private static string? AgentDetail(AgentBreakdown agent) => agent.Kind switch
    {
        AgentKind.Subagent when agent.ReportTokens is { } report => $"its report: {Formats.Tokens(report)}",
        AgentKind.ClaudeCode => "calls it doesn't show",
        _ => null,
    };

    private string ModelName(string model) => _tab.ModelName(model);

    private DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _services.Time.LocalTimeZone);

    private static double Scaled(double value, double largest) => largest > 0 ? value / largest : 0;

    private static string Percent(double share, int decimals = 0) =>
        (share * 100).ToString(decimals == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + "%";

    internal static readonly IReadOnlyList<SaveFileType> ExportTypes = [new("Comma-separated values", "csv")];

    /// <summary>A bar of cache reads only, for what tools' results cost read again.</summary>
    public static readonly KindValues CacheReadOnly = new(1, 0, 0, 0);

    /// <summary><b>Export…</b>: the turns and their calls as CSV, with each turn's first line of prompt.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_breakdown is not { } b)
        {
            return;
        }
        var name = string.Concat(_tab.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (await _services.Platform.PickSaveFileAsync("Export the token breakdown", $"{(name.Length > 0 ? name : "Tokens")} tokens.csv", ExportTypes) is not { } path)
        {
            return;
        }
        var csv = ToCsv(b, PromptsOf(b.Turns));
        try
        {
            await Task.Run(() => AtomicFile.WriteAllText(path, csv));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing written: the window stays as it was.
        }
    }

    /// <summary>The turns and their calls as CSV: a line per call, or per model for a turn recorded without its calls.</summary>
    internal static string ToCsv(TokenBreakdown breakdown, IReadOnlyDictionary<TurnBreakdown, string> prompts)
    {
        var csv = new StringBuilder("turn,ended,prompt,call,kind,agent,model,input,cache_read,cache_write_5m,cache_write_1h,output,cost_usd\n");
        foreach (var turn in breakdown.Turns)
        {
            var head = $"{turn.Number},{turn.At.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ},{Quote(prompts.GetValueOrDefault(turn) ?? "")}";
            if (turn.HasCalls)
            {
                foreach (var c in turn.Calls)
                {
                    csv.Append(CultureInfo.InvariantCulture,
                        $"{head},{c.Seq},{c.Kind},{Quote(c.AgentType ?? (c.Agent is null ? "main" : "subagent"))},{Quote(c.Model)},{c.Input},{c.CacheRead},{c.Write5m},{c.Write1h},{c.Output},{c.CostUsd:0.######}\n");
                }
            }
            else
            {
                foreach (var r in turn.Records)
                {
                    csv.Append(CultureInfo.InvariantCulture, $"{head},,,,{Quote(r.Model)},{r.Input},{r.CacheRead},{r.CacheWrite},,{r.Output},{r.CostUsd:0.######}\n");
                }
            }
        }
        return csv.ToString();

        static string Quote(string text) => text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }

    public void Dispose()
    {
        if (_services.Usage is { } usage)
        {
            usage.TurnRecorded -= OnTurnRecorded;
        }
    }
}
