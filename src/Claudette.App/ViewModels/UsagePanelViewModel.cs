using System.Globalization;
using Claudette.App.Controls;
using Claudette.App.Services;
using Claudette.Core;
using Claudette.Core.Sessions;
using Claudette.Core.Settings;
using Claudette.Usage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

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

    /// <summary>Turns recorded before Claudette kept each turn's folder.</summary>
    public bool IsEarlier { get; init; }

    public string Tooltip =>
        (Folder ?? (IsEarlier ? "Turns recorded before Claudette kept each turn's folder." : $"{Name} isn't on this machine."))
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

/// <summary>The period the Usage panel's <b>Projects</b> list covers.</summary>
public enum ProjectPeriod
{
    SessionWindow,
    Week,
    Month,
}

/// <summary>A choice in the Projects list's period picker, shown by its label.</summary>
public sealed record ProjectPeriodChoice(ProjectPeriod Period)
{
    public override string ToString() => Period switch
    {
        ProjectPeriod.SessionWindow => "This session window",
        ProjectPeriod.Month => "The last 30 days",
        _ => "This week",
    };
}

/// <summary>A past session window or week, with the highest usage it reached.</summary>
public sealed record PastWindowRow(string When, string Peak, double Percent);

/// <summary>
/// The Usage panel (DESIGN.md §6, "Burn trendline"): larger charts of the current session and the past week, tokens
/// per tab for the current window, and past sessions and weeks from the usage history.
/// </summary>
public sealed partial class UsagePanelViewModel : ViewModelBase
{
    private static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    private readonly AppServices _services;
    private readonly UsageTracker _tracker;
    private readonly UsageViewModel _header;
    private readonly Func<string, string?> _tabName;

    public UsagePanelViewModel(AppServices services, UsageTracker tracker, UsageViewModel header, Func<string, string?> tabName)
    {
        _services = services;
        _tracker = tracker;
        _header = header;
        _tabName = tabName;
        Refresh();
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

    [ObservableProperty]
    public partial IReadOnlyList<TabBurnRow> Tabs { get; set; } = [];

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Tokens per project over <see cref="SelectedProjectPeriod"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects))]
    public partial IReadOnlyList<ProjectBurnRow> Projects { get; set; } = [];

    public bool HasProjects => Projects.Count > 0;

    /// <summary>What the Projects list counts: other machines' synced tabs too, while machines share their usage (DESIGN.md §6).</summary>
    public string ProjectsNote => _services.Settings.Usage.ShareThroughLibrary
        ? "Tokens used in each project, across all its tabs here and the tabs other machines sync to the session library."
        : "Tokens used in each folder, across all its tabs.";

    /// <summary>
    /// Tokens and estimated spend per account over <see cref="SelectedProjectPeriod"/>, once there's an account besides
    /// Default (DESIGN.md §4, "Accounts").
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccounts))]
    public partial IReadOnlyList<AccountBurnRow> Accounts { get; set; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    public IReadOnlyList<ProjectPeriodChoice> ProjectPeriods { get; } = [.. Enum.GetValues<ProjectPeriod>().Select(p => new ProjectPeriodChoice(p))];

    /// <summary>The week by default, like the chart above it.</summary>
    [ObservableProperty]
    public partial ProjectPeriodChoice SelectedProjectPeriod { get; set; } = new(ProjectPeriod.Week);

    partial void OnSelectedProjectPeriodChanged(ProjectPeriodChoice value) => Refresh();

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
        // Tokens per tab since the current session window started.
        var windowStart = snapshot?.Session?.ResetsAt is { } sessionReset ? sessionReset - SessionWindow : now - SessionWindow;
        var projectsFrom = SelectedProjectPeriod.Period switch
        {
            ProjectPeriod.SessionWindow => windowStart,
            ProjectPeriod.Month => now - TimeSpan.FromDays(30),
            _ => weekStart,
        };
        (IReadOnlyList<UsageSample> Samples, IReadOnlyList<TabTokenSum> Tabs, IReadOnlyList<ProjectTokenSum> Projects, IReadOnlyList<AccountTokenSum> Accounts,
            IReadOnlyList<WindowPeak> Sessions, IReadOnlyList<WindowPeak> Weeks, IReadOnlyDictionary<string, string> Machines) data;
        try
        {
            data = await Task.Run(() => (
                store.GetSamples(weekStart, now),
                store.GetTokensByTab(windowStart, planOnly: true),
                store.GetTokensByProject(projectsFrom),
                store.GetTokensByAccount(projectsFrom),
                store.GetPastWindows(UsageWindow.Session, now),
                store.GetPastWindows(UsageWindow.Weekly, now),
                store.GetMachineNames()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The history may be closing as Claudette quits, or damaged (it starts again by itself): show what's there.
            return;
        }
        if (generation != _refreshes)
        {
            return;
        }
        WeekEnd = weekEnd;
        WeekStart = weekStart;
        WeekPoints = data.Samples.Where(s => s.WeeklyPercent is not null).Select(s => new ChartPoint(s.Timestamp, s.WeeklyPercent!.Value)).ToArray();
        Tabs = TabBurnRow.From(data.Tabs, _tabName, data.Machines);
        OnPropertyChanged(nameof(HasTabs));
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
}
