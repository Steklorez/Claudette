using System.Globalization;
using Deventry.App.Controls;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using Deventry.Usage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>A tab's tokens in the current session window, for "which tab is burning the most".</summary>
/// <param name="Share">The tab's part of all the tabs' tokens in the window, from 0 to 1.</param>
public sealed record TabBurnRow(string Name, string Tokens, string Cost, string Turns, double Share)
{
    /// <summary>"41%", for the detailed header's busiest tabs (DESIGN.md §6).</summary>
    public string ShareText => string.Create(CultureInfo.CurrentCulture, $"{Share * 100:0}%");

    /// <summary>Which machine the tab is on, for another machine's (DESIGN.md §6, "Sharing across machines"); null for this one's.</summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// One row per tab, the heaviest first. <paramref name="tabName"/> names open tabs; a closed one keeps the last name
    /// the usage history has for it. Another machine's tab says which machine it's on.
    /// </summary>
    /// <param name="machineNames">The names of the machines that shared turns, by id.</param>
    public static IReadOnlyList<TabBurnRow> From(IReadOnlyList<TabTokenSum> sums, Func<string, string?> tabName, IReadOnlyDictionary<string, string>? machineNames = null)
    {
        var total = Math.Max(1, sums.Sum(s => s.Total));
        return sums
            .OrderByDescending(s => s.Total)
            .Select(s => s.Machine is { } machine
                ? new TabBurnRow($"{s.Name ?? "A tab"} · {MachineNames.Of(machine, machineNames)}", TokenTotals.Short(s.Total), $"${s.CostUsd:0.00}", TurnsText(s.Turns), (double)s.Total / total)
                {
                    Tooltip = $"On {MachineNames.Of(machine, machineNames)}",
                }
                : new TabBurnRow(
                    // Turns recorded before the history kept names have none.
                    tabName(s.TabId) ?? s.Name ?? "A closed tab",
                    TokenTotals.Short(s.Total),
                    $"${s.CostUsd:0.00}",
                    TurnsText(s.Turns),
                    (double)s.Total / total))
            .ToArray();
    }

    internal static string TurnsText(int turns) => $"{turns} turn{(turns == 1 ? "" : "s")}";
}

/// <summary>A project's tokens over the period the Usage panel's <b>Projects</b> list shows (DESIGN.md §6).</summary>
/// <param name="Folder">This machine's folder for it, for the tooltip; null for turns recorded before projects were kept, and a project only other machines have.</param>
/// <param name="Share">The project's part of all the projects' tokens in the period, from 0 to 1.</param>
public sealed record ProjectBurnRow(string Name, string? Folder, string Tokens, string Cost, string Turns, double Share)
{
    /// <summary>What each machine spent on it, a line each, when more than one did (DESIGN.md §6, "Sharing across machines").</summary>
    public string? Split { get; init; }

    /// <summary>Turns recorded before Deventry kept each turn's folder.</summary>
    public bool IsEarlier { get; init; }

    public string Tooltip =>
        (Folder ?? (IsEarlier ? "Turns recorded before Deventry kept each turn's folder." : $"{Name} isn't on this machine."))
        + (Split is null ? "" : $"\n{Split}");

    /// <summary>
    /// One row per project, the heaviest first, adding up every machine's turns in it. Named by its folder here, or the
    /// name another machine gave it; the parent folder, or the machine, tells apart two of a name.
    /// </summary>
    /// <param name="machineNames">The names of the machines that shared turns, by id.</param>
    public static IReadOnlyList<ProjectBurnRow> From(IReadOnlyList<ProjectTokenSum> sums, IReadOnlyDictionary<string, string>? machineNames = null)
    {
        var total = Math.Max(1, sums.Sum(s => s.Total));
        var projects = sums
            .GroupBy(s => s.Key ?? s.Project, StringComparer.Ordinal)
            .Select(group =>
            {
                var parts = group.ToArray();
                var folder = parts.FirstOrDefault(s => s.Machine is null && s.Project is not null)?.Project;
                var name = parts.FirstOrDefault(s => s.Machine is null)?.Name ?? (folder is null ? null : Formats.FolderName(folder))
                    ?? parts.Select(s => s.Name).FirstOrDefault(n => n is not null);
                return (Key: group.Key, Parts: parts, Folder: folder, Name: name);
            })
            .OrderByDescending(p => p.Parts.Sum(s => s.Total))
            .ToArray();
        return projects
            .Select(p =>
            {
                var tokens = p.Parts.Sum(s => s.Total);
                var cost = p.Parts.Sum(s => s.CostUsd);
                var turns = p.Parts.Sum(s => s.Turns);
                var name = p.Key is null ? "Earlier turns"
                    : p.Name is null ? "A project"
                    : projects.Count(o => o.Key is not null && o.Name == p.Name) <= 1 ? p.Name
                    : p.Folder is not null && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p.Folder)) is { Length: > 0 } parent ? $"{p.Name} ({Formats.FolderName(parent)})"
                    : p.Parts.FirstOrDefault(s => s.Machine is not null)?.Machine is { } machine ? $"{p.Name} ({MachineNames.Of(machine, machineNames)})"
                    : p.Name;
                return new ProjectBurnRow(name, p.Folder, TokenTotals.Short(tokens), $"${cost:0.00}", TabBurnRow.TurnsText(turns), (double)tokens / total)
                {
                    Split = MachineNames.Split(p.Parts.Select(s => (s.Machine, s.CostUsd, s.Turns)), machineNames, c => $"${c:0.00}"),
                    IsEarlier = p.Key is null,
                };
            })
            .ToArray();
    }
}

/// <summary>
/// An account's tokens and estimated spend over the period the Usage panel's <b>Projects</b> list shows (DESIGN.md §4,
/// "Accounts").
/// </summary>
/// <param name="Share">The account's part of all the accounts' tokens in the period, from 0 to 1.</param>
public sealed record AccountBurnRow(string Name, string Tokens, string Cost, string Turns, double Share)
{
    /// <summary>What each machine spent under it, a line each, when more than one did.</summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// One row per account, the heaviest first; Default by name, and one removed since as such. Another machine's turns
    /// under an API-key account count with this machine's account of the same name, or as an account of their own
    /// (DESIGN.md §6, "Sharing across machines").
    /// </summary>
    /// <param name="machineNames">The names of the machines that shared turns, by id.</param>
    public static IReadOnlyList<AccountBurnRow> From(IReadOnlyList<AccountTokenSum> sums, AccountsService accounts, IReadOnlyDictionary<string, string>? machineNames = null)
    {
        var total = Math.Max(1, sums.Sum(s => s.Total));
        return sums
            .GroupBy(s => s switch
            {
                { Account: null } => (Key: "", Name: AccountsService.DefaultName),
                { Machine: null } => (Key: $"id:{s.Account}", Name: accounts.Find(s.Account)?.Name ?? "A removed account"),
                _ when accounts.FindByName(s.AccountName) is { } same => (Key: $"id:{same.Id}", Name: same.Name),
                _ => (Key: $"name:{s.AccountName?.Trim().ToLowerInvariant()}", Name: s.AccountName?.Trim() is { Length: > 0 } name ? name : "An account"),
            })
            .Select(group => (group.Key.Name, Parts: group.ToArray()))
            .OrderByDescending(a => a.Parts.Sum(s => s.Total))
            .Select(a =>
            {
                var tokens = a.Parts.Sum(s => s.Total);
                var turns = a.Parts.Sum(s => s.Turns);
                return new AccountBurnRow(a.Name, TokenTotals.Short(tokens), Formats.Dollars(a.Parts.Sum(s => s.CostUsd)), TabBurnRow.TurnsText(turns), (double)tokens / total)
                {
                    Tooltip = MachineNames.Split(a.Parts.Select(s => (s.Machine, s.CostUsd, s.Turns)), machineNames, Formats.Dollars),
                };
            })
            .ToArray();
    }
}

/// <summary>How the Usage panel names the machines that shared turns (DESIGN.md §6, "Sharing across machines").</summary>
internal static class MachineNames
{
    public static string Of(string machine, IReadOnlyDictionary<string, string>? names) =>
        names is not null && names.TryGetValue(machine, out var name) ? name : "another machine";

    /// <summary>
    /// What each machine spent, the most first, a line each: "This machine: $3.10 · 12 turns". Null when only one machine
    /// did, since the row says it already.
    /// </summary>
    public static string? Split(IEnumerable<(string? Machine, double CostUsd, int Turns)> parts, IReadOnlyDictionary<string, string>? names, Func<double, string> cost)
    {
        var byMachine = parts
            .GroupBy(p => p.Machine)
            .Select(g => (Machine: g.Key, CostUsd: g.Sum(p => p.CostUsd), Turns: g.Sum(p => p.Turns)))
            .OrderByDescending(m => m.CostUsd)
            .ToArray();
        return byMachine.Length < 2
            ? null
            : string.Join("\n", byMachine.Select(m => $"{(m.Machine is { } id ? Of(id, names) : "This machine")}: {cost(m.CostUsd)} · {TabBurnRow.TurnsText(m.Turns)}"));
    }
}

/// <summary>
/// A tab's row in the Usage panel's <b>Tabs</b> (DESIGN.md §18, "The Usage panel"): its tokens and cost over the period,
/// its cost by kind, its turns and expired caches, and the way to its breakdown, open or closed.
/// </summary>
/// <param name="Detail">Its project, <i>closed</i> or the machine it's on, and an API key's account: <i>starfall · closed</i>.</param>
/// <param name="Scale">Its cost against the costliest tab's, for its bar.</param>
/// <param name="Expired">*2 expired caches*; null when it had none.</param>
public sealed record TabTokensRow(string Name, string Detail, KindValues Costs, double Scale, string Tokens, string Cost, string Turns, string? Expired, HistoryTabBreakdown Tab)
{
    public bool HasExpired => Expired is not null;
}

/// <summary>A model's row in the Usage panel's <b>Models</b>: its calls, the kinds that cost, and its cost.</summary>
public sealed record TokenModelRow(string Name, string Calls, string CacheRead, string CacheWrite, string Output, string Cost);

/// <summary>A choice in the Usage panel's <b>Tokens over</b>, shown by its label.</summary>
public sealed record UsagePeriodChoice(UsagePanelPeriod Period)
{
    public override string ToString() => Period switch
    {
        UsagePanelPeriod.SessionWindow => "This session window",
        UsagePanelPeriod.Month => "The last 30 days",
        _ => "This week",
    };
}

/// <summary>A past session window or week, with the highest usage it reached.</summary>
public sealed record PastWindowRow(string When, string Peak, double Percent);

/// <summary>
/// The Usage panel (DESIGN.md §6, "Burn trendline"): larger charts of the current session and the past week; over the
/// period <b>Tokens over</b> chooses, where the tokens went, and the tokens per tab, project, account, model and tool
/// (DESIGN.md §18, "The Usage panel"); and past sessions and weeks from the usage history.
/// </summary>
public sealed partial class UsagePanelViewModel : ViewModelBase
{
    private static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    private readonly AppServices _services;
    private readonly UsageTracker _tracker;
    private readonly UsageViewModel _header;
    private readonly Func<string, string?> _tabName;
    private readonly Action<HistoryTabBreakdown>? _openBreakdown;

    /// <param name="tabName">An open tab's name, by its id; null for a tab that isn't open.</param>
    /// <param name="openBreakdown">Opens a tab's token breakdown, from its row's <b>›</b>.</param>
    public UsagePanelViewModel(AppServices services, UsageTracker tracker, UsageViewModel header, Func<string, string?> tabName,
        Action<HistoryTabBreakdown>? openBreakdown = null)
    {
        _services = services;
        _tracker = tracker;
        _header = header;
        _tabName = tabName;
        _openBreakdown = openBreakdown;
        // Choosing it reads the history.
        SelectedPeriod = Periods.FirstOrDefault(p => p.Period == services.State.UsagePanelPeriod) ?? Periods[0];
    }

    /// <summary>Counts refreshes, so a slow one that finishes after a later one doesn't overwrite it.</summary>
    private int _refreshes;

    public UsageViewModel Header => _header;

    [ObservableProperty]
    public partial IReadOnlyList<ChartPoint> WeekPoints { get; set; } = [];

    [ObservableProperty]
    public partial DateTimeOffset WeekStart { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset WeekEnd { get; set; }

    // ---- Tokens over the period (DESIGN.md §18, "The Usage panel") --------------------------------------------------

    public IReadOnlyList<UsagePeriodChoice> Periods { get; } = [.. Enum.GetValues<UsagePanelPeriod>().Select(p => new UsagePeriodChoice(p))];

    /// <summary>The period every token section covers. It starts on this session window, and is remembered on this machine.</summary>
    [ObservableProperty]
    public partial UsagePeriodChoice SelectedPeriod { get; set; }

    partial void OnSelectedPeriodChanged(UsagePeriodChoice value)
    {
        if (_services.State.UsagePanelPeriod != value.Period)
        {
            _services.State.UsagePanelPeriod = value.Period;
            _services.SaveState();
        }
        Refresh();
    }

    /// <summary>The period has turns: the token sections show.</summary>
    [ObservableProperty]
    public partial bool HasTurns { get; set; }

    [ObservableProperty]
    public partial KindValues TokenValues { get; set; } = KindValues.Zero;

    [ObservableProperty]
    public partial KindValues CostValues { get; set; } = KindValues.Zero;

    /// <summary>Each kind of token over the period, its tokens and cost and their shares.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TokenKindRow> Kinds { get; set; } = [];

    /// <summary>*1.2M tokens · about $3.40 · 14 turns in 3 tabs*.</summary>
    [ObservableProperty]
    public partial string TotalsText { get; set; } = "";

    /// <summary>*4 turns began after the cache expired: $2.73 to read their conversations again.*; null when none did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpiredText))]
    public partial string? ExpiredText { get; set; }

    public bool HasExpiredText => ExpiredText is not null;

    /// <summary>Every tab with turns in the period, the costliest first: open, closed and other machines' synced tabs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabs))]
    public partial IReadOnlyList<TabTokensRow> Tabs { get; set; } = [];

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Per model, the costliest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModels))]
    public partial IReadOnlyList<TokenModelRow> Models { get; set; } = [];

    public bool HasModels => Models.Count > 0;

    /// <summary>Per tool, those whose results were read again the most first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTools))]
    public partial IReadOnlyList<TokenToolRow> Tools { get; set; } = [];

    public bool HasTools => Tools.Count > 0;

    /// <summary>A tab's <b>›</b>: its token breakdown, open or closed.</summary>
    [RelayCommand]
    private void OpenTab(TabTokensRow? row)
    {
        if (row is not null)
        {
            _openBreakdown?.Invoke(row.Tab);
        }
    }

    /// <summary>Tokens per project over <see cref="SelectedPeriod"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects))]
    public partial IReadOnlyList<ProjectBurnRow> Projects { get; set; } = [];

    public bool HasProjects => Projects.Count > 0;

    /// <summary>What the Projects list counts: other machines' synced tabs too, while machines share their usage (DESIGN.md §6).</summary>
    public string ProjectsNote => _services.Settings.Usage.ShareThroughLibrary
        ? "Tokens used in each project, across all its tabs here and the tabs other machines sync to the session library."
        : "Tokens used in each folder, across all its tabs.";

    /// <summary>
    /// Tokens and estimated spend per account over <see cref="SelectedPeriod"/>, once there's an account besides Default
    /// (DESIGN.md §4, "Accounts").
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccounts))]
    public partial IReadOnlyList<AccountBurnRow> Accounts { get; set; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    [ObservableProperty]
    public partial IReadOnlyList<PastWindowRow> PastSessions { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PastWindowRow> PastWeeks { get; set; } = [];

    /// <summary>How many past sessions and weeks are listed before <b>Show more</b>.</summary>
    public const int SessionsPage = 30;

    public const int WeeksPage = 12;

    private IReadOnlyList<PastWindowRow> _pastSessions = [];
    private IReadOnlyList<PastWindowRow> _pastWeeks = [];
    private int _sessionsShown = SessionsPage;
    private int _weeksShown = WeeksPage;

    public string MoreSessionsText => MoreText(_pastSessions.Count - _sessionsShown, SessionsPage);

    public bool HasMoreSessions => _pastSessions.Count > _sessionsShown;

    public string MoreWeeksText => MoreText(_pastWeeks.Count - _weeksShown, WeeksPage);

    public bool HasMoreWeeks => _pastWeeks.Count > _weeksShown;

    [RelayCommand]
    private void ShowMoreSessions()
    {
        _sessionsShown += SessionsPage;
        ShowPastWindows();
    }

    [RelayCommand]
    private void ShowMoreWeeks()
    {
        _weeksShown += WeeksPage;
        ShowPastWindows();
    }

    private void ShowPastWindows()
    {
        PastSessions = _pastSessions.Take(_sessionsShown).ToArray();
        PastWeeks = _pastWeeks.Take(_weeksShown).ToArray();
        OnPropertyChanged(nameof(MoreSessionsText));
        OnPropertyChanged(nameof(HasMoreSessions));
        OnPropertyChanged(nameof(MoreWeeksText));
        OnPropertyChanged(nameof(HasMoreWeeks));
    }

    private static string MoreText(int left, int page) => left > page ? $"Show {page} more ({left} left)" : $"Show {left} more";

    public string HistoryNote => _services.Settings.Usage.KeepHistory is RetentionPeriod.Forever
        ? "Usage history is kept forever (Settings → Usage)."
        : $"Usage history is kept for {_services.Settings.Usage.KeepHistory.Label().ToLowerInvariant()} (Settings → Usage).";

    /// <summary>Reloads the panel from the usage history, in the background (after each turn and each import).</summary>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>
    /// Queries the usage history off the UI thread (a group-by over all of it, for the past windows), then shows the
    /// result.
    /// </summary>
    public async Task RefreshAsync()
    {
        var generation = ++_refreshes;
        var now = _services.Time.GetUtcNow();
        var store = _tracker.Store;
        var snapshot = _tracker.Current;

        // The week: the weekly limit's own window when known, else the last 7 days.
        var weekEnd = snapshot?.WeeklyAll?.ResetsAt is { } weekReset && weekReset > now ? weekReset : now;
        var weekStart = weekEnd - TimeSpan.FromDays(7);
        var windowStart = snapshot?.Session?.ResetsAt is { } sessionReset ? sessionReset - SessionWindow : now - SessionWindow;
        var period = SelectedPeriod.Period;
        var from = period switch
        {
            UsagePanelPeriod.SessionWindow => windowStart,
            UsagePanelPeriod.Month => now - TimeSpan.FromDays(30),
            _ => weekStart,
        };
        (IReadOnlyList<UsageSample> Samples, TokenBreakdown Breakdown, IReadOnlyDictionary<string, string> Names, IReadOnlyList<ProjectTokenSum> Projects,
            IReadOnlyList<AccountTokenSum> Accounts, IReadOnlyList<WindowPeak> Sessions, IReadOnlyList<WindowPeak> Weeks, IReadOnlyDictionary<string, string> Machines) data;
        try
        {
            data = await Task.Run(() => (
                store.GetSamples(weekStart, now),
                // With no end, as the sections beside it: another machine's clock may run ahead of this one's.
                TokenBreakdown.ReadPeriod(store, from, DateTimeOffset.MaxValue),
                store.GetTabNames(),
                store.GetTokensByProject(from),
                store.GetTokensByAccount(from),
                store.GetPastWindows(UsageWindow.Session, now),
                store.GetPastWindows(UsageWindow.Weekly, now),
                store.GetMachineNames()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The history may be closing as Deventry quits, or damaged (it starts again by itself): show what's there.
            return;
        }
        if (generation != _refreshes)
        {
            return;
        }
        WeekEnd = weekEnd;
        WeekStart = weekStart;
        WeekPoints = data.Samples.Where(s => s.WeeklyPercent is not null).Select(s => new ChartPoint(s.Timestamp, s.WeeklyPercent!.Value)).ToArray();
        ShowTokens(data.Breakdown, data.Names, data.Machines);
        Projects = ProjectBurnRow.From(data.Projects, data.Machines);
        Accounts = _services.Accounts.HasAccounts || data.Accounts.Any(a => a.Account is not null)
            ? AccountBurnRow.From(data.Accounts, _services.Accounts, data.Machines)
            : [];
        OnPropertyChanged(nameof(ProjectsNote));

        // Past windows, as far back as the history goes; the lists show a page at a time.
        _pastSessions = data.Sessions
            .Select(p =>
            {
                var at = p.ResetsAt.ToLocalTime();
                return Row($"Session ending {at:ddd MMM d}, {at:t}", p.PeakPercent);
            })
            .ToArray();
        _pastWeeks = data.Weeks
            .Select(p => Row($"Week ending {p.ResetsAt.ToLocalTime():ddd MMM d}", p.PeakPercent))
            .ToArray();
        ShowPastWindows();
        OnPropertyChanged(nameof(HistoryNote));

        static PastWindowRow Row(string when, double peak) => new(when, $"peaked at {peak:0}%", Math.Clamp(peak, 0, 100));
    }

    /// <summary>The token sections from the period's breakdown: where the tokens went, and per tab, model and tool.</summary>
    private void ShowTokens(TokenBreakdown b, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, string> machines)
    {
        HasTurns = b.Turns.Count > 0;
        TokenValues = KindValues.Of(b.Tokens);
        CostValues = KindValues.Of(b.Costs);
        Kinds = TokenBreakdownViewModel.KindRows(b.Tokens, b.Costs);
        var tabs = b.ByTab.Count;
        TotalsText = string.Create(CultureInfo.CurrentCulture,
            $"{Formats.Tokens(b.Tokens.Total)} tokens · about {Formats.Dollars(b.CostUsd)} · {TabBurnRow.TurnsText(b.Turns.Count)} in {tabs} tab{(tabs == 1 ? "" : "s")}");
        var (expired, expiredCost) = b.CacheExpired;
        ExpiredText = expired == 0 ? null
            : expired == 1 ? $"1 turn began after the cache expired: {Formats.Dollars(expiredCost)} to read its conversation again."
            : string.Create(CultureInfo.CurrentCulture, $"{expired} turns began after the cache expired: {Formats.Dollars(expiredCost)} to read their conversations again.");

        var costliest = b.ByTab.Count > 0 ? b.ByTab.Max(t => t.CostUsd) : 0;
        Tabs = [.. b.ByTab.Select(t => TabRow(t, costliest, names, machines))];
        Models = [.. b.ByModel.Select(m => new TokenModelRow(Formats.ModelName(m.Model), m.Calls > 0 ? m.Calls.ToString("N0", CultureInfo.CurrentCulture) : "–",
            Formats.Tokens(m.Tokens.CacheRead), Formats.Tokens(m.Tokens.CacheWrite), Formats.Tokens(m.Tokens.Output), TokenBreakdownViewModel.Dollars(m.CostUsd)))];
        var mostRead = b.ByTool.Count > 0 ? b.ByTool.Max(t => t.ReadAgain) : 0;
        Tools = [.. b.ByTool.Select(t => new TokenToolRow(t.Tool, t.Calls.ToString("N0", CultureInfo.CurrentCulture), Formats.Tokens(t.Added),
            mostRead > 0 ? (double)t.ReadAgain / mostRead : 0, Formats.Tokens(t.ReadAgain), TokenBreakdownViewModel.Dollars(t.ReadAgainCostUsd)))];
    }

    /// <summary>A tab's row: named as it is when it's open, by the name it last had when it isn't, and another machine's by its machine too.</summary>
    private TabTokensRow TabRow(TabBreakdown tab, double costliest, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, string> machines)
    {
        var open = tab.Machine is null ? _tabName(tab.TabId) : null;
        var name = open ?? names.GetValueOrDefault(tab.TabId) ?? (tab.Machine is null ? "A closed tab" : "A tab");
        var machine = tab.Machine is { } id ? MachineNames.Of(id, machines) : null;
        var project = tab.ProjectName ?? (tab.Project is { } folder ? Formats.FolderName(folder) : null);
        var account = tab.Account is null ? null
            : tab.Machine is null && _services.Accounts.Find(tab.Account) is { } mine ? mine.Name
            : tab.AccountName?.Trim() is { Length: > 0 } named ? named : "an API key";
        var detail = new List<string>();
        if (project is not null)
        {
            detail.Add(project);
        }
        if ((machine ?? (open is null ? "closed" : null)) is { } where)
        {
            detail.Add(where);
        }
        if (account is not null)
        {
            detail.Add(account);
        }
        return new TabTokensRow(name, string.Join(" · ", detail), KindValues.Of(tab.Costs), costliest > 0 ? tab.CostUsd / costliest : 0,
            Formats.Tokens(tab.Tokens.Total), TokenBreakdownViewModel.Dollars(tab.CostUsd), TabBurnRow.TurnsText(tab.Turns),
            tab.CacheExpired switch
            {
                0 => null,
                1 => "1 expired cache",
                var n => string.Create(CultureInfo.CurrentCulture, $"{n} expired caches"),
            },
            new HistoryTabBreakdown(tab.TabId, name, machine, project, tab.SessionId, tab.Account is null));
    }
}
