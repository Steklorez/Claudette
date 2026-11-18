using System.Collections.ObjectModel;
using Deventry.Core;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A favorite folder in Settings → Tabs, in the order the new tab picker lists them.</summary>
public sealed record FavoriteFolderRow(string Path, string Name);

/// <summary>
/// The keys Tab settings… overrides go by (DESIGN.md §14, "Per-tab overrides"), for the settings that say "2 tabs have
/// their own".
/// </summary>
public static class TabOverrideKeys
{
    public const string Model = "model";
    public const string Effort = "effort";
    public const string PermissionMode = "permissionMode";
    public const string Subagents = "subagents";
    public const string ContinueAfterLimitReset = "continueAfterLimitReset";
    public const string UseUsageCredits = "useUsageCredits";
    public const string CheckIns = "checkIns";
    public const string ProcessMonitor = "processMonitor";
    public const string FoldChains = "foldChains";

    /// <summary>Whether <paramref name="overrides"/> has its own value for the setting <paramref name="key"/> names.</summary>
    public static bool Has(TabOverrides overrides, string key) => key switch
    {
        Model => overrides.Model is not null,
        Effort => overrides.Effort is not null,
        PermissionMode => overrides.PermissionMode is not null,
        Subagents => overrides.Subagents is not null,
        ContinueAfterLimitReset => overrides.ContinueAfterLimitReset is not null,
        UseUsageCredits => overrides.UseUsageCredits is not null,
        CheckIns => overrides.CheckIns is not null,
        ProcessMonitor => overrides.ShowProcessMonitor is not null,
        FoldChains => overrides.FoldChains is not null,
        _ => false,
    };
}

/// <summary>
/// Settings → Tabs (DESIGN.md §14): what every tab does, which its Tab settings… can change for it: what happens at a
/// usage limit, check-ins on long turns and the process monitor; the new tab picker's recent and favorite folders
/// (DESIGN.md §4, "Opening a tab"); and Claude Code's projects in the sidebar. What a new tab starts with is its agent's
/// page's (<see cref="ClaudeCodePage"/>, <see cref="CodexPage"/>), and subagents are All agents' (<see cref="AllAgentsPage"/>).
/// </summary>
public sealed partial class TabsPage : SettingsPage
{
    public TabsPage(SettingsContext context) : base(context, SettingsCategory.Tabs)
    {
        Section("usageLimits", "At a usage limit",
        [
            Setting("usage.continueAfterLimitReset", "Continue tasks when a usage limit resets", s => s.Usage.ContinueAfterLimitReset, (s, v) => s.Usage.ContinueAfterLimitReset = v,
                "The tab waits for the limit to reset, then asks Claude to carry on.",
                "When a usage limit stops Claude mid-task, the tab waits for the limit to reset, then asks Claude to continue from where it left off.",
                ["auto-continue", "resume", "limit"], TabOverrideKeys.ContinueAfterLimitReset),
            Setting("usage.useUsageCredits", "Use usage credits once a limit is used up", s => s.Usage.UseUsageCredits, (s, v) => s.Usage.UseUsageCredits = v,
                "Off, tabs stop at the limit instead, and wait for it to reset.",
                "When your plan has usage credits turned on, they pay for Claude once your session or weekly limit is used up. Each tab can change this from its menu too, so only the work that matters spends them.",
                ["extra usage", "credits", "overage", "limit"], TabOverrideKeys.UseUsageCredits),
        ]);
        Section("checkIns", "Check-ins on long turns",
        [
            Setting("checkIns.enabled", "Check in on long turns", s => s.CheckIns.Enabled, (s, v) => s.CheckIns.Enabled = v,
                "Deventry asks Claude how it's going, so a stuck tab doesn't burn usage unnoticed.", keywords: ["stuck", "long turn"],
                overrideKey: TabOverrideKeys.CheckIns),
            Setting("checkIns.runTimeMinutes", "After the turn has run (minutes)", s => s.CheckIns.RunTimeMinutes, (s, v) => s.CheckIns.RunTimeMinutes = v,
                "0 turns this off.", keywords: ["check-in"]),
            Setting("checkIns.quietTimeMinutes", "After no output for (minutes)", s => s.CheckIns.QuietTimeMinutes, (s, v) => s.CheckIns.QuietTimeMinutes = v,
                "0 turns this off.", keywords: ["check-in", "quiet"]),
            Setting("checkIns.message", "Message", s => s.CheckIns.Message, (s, v) => s.CheckIns.Message = v,
                "What Deventry sends Claude.", keywords: ["check-in message"]),
            Setting("checkIns.notify", "Notify me when a check-in is sent", s => s.CheckIns.Notify, (s, v) => s.CheckIns.Notify = v,
                keywords: ["check-in", "notification"]),
        ]);
        Section("processMonitor", "Process monitor",
        [
            Setting("processes.showMonitor", "Show the process monitor", s => s.Processes.ShowMonitor, (s, v) => s.Processes.ShowMonitor = v,
                "The processes each tab starts, with their CPU and memory.",
                "Test runs, dev servers, builds and MCP servers, in the message box's bar and the Processes tool window. Closing a tab stops the processes it started, unless you choose to leave them running.",
                ["processes", "cpu", "memory"], TabOverrideKeys.ProcessMonitor),
            Setting("processes.refreshSeconds", "Refresh every (seconds)", s => s.Processes.RefreshSeconds, (s, v) => s.Processes.RefreshSeconds = v,
                keywords: ["process monitor", "interval"]),
            Setting("processes.showCommandLines", "Show command lines", s => s.Processes.ShowCommandLines, (s, v) => s.Processes.ShowCommandLines = v,
                "They sometimes hold tokens or passwords: turn this off if others can see your screen.", keywords: ["process monitor", "privacy"]),
        ]);
        Section("picker", "New tab picker",
        [
            Setting("newTabs.recentFolderLimit", "Recent folders to keep", s => s.NewTabs.RecentFolderLimit, (s, v) => s.NewTabs.RecentFolderLimit = v,
                keywords: ["history", "recent"]),
            Info("state.clearRecentFolders", "Clear recent folders", "Favorites are kept.", keywords: ["recent", "history"]),
            // This machine's folders, not a setting: never reset.
            Setting("state.favoriteFolders", "Favorite folders", null, null, "Always at the top of the new tab picker.", keywords: ["pinned folders", "favourites"]),
        ]);
        Section("claudeProjects", "Claude Code's projects",
        [
            Setting("sidebar.showClaudeProjects", "Show Claude Code's projects in the sidebar", s => s.Sidebar.ShowClaudeProjects, (s, v) => s.Sidebar.ShowClaudeProjects = v,
                "Every folder Claude Code has sessions in gets a group, with its past sessions under it, tab or no tab.",
                "The sessions are the ones History lists: Claude Code's own on this machine, including ones started in a terminal, and the session library's. A group's past sessions fold away under its tabs; click one to pick it up in a new tab. Turning this off closes the groups that have nothing open.",
                ["claude projects", "past sessions", "folders", "history", "sidebar"]),
            // This machine's choices, not a setting: never reset.
            Info("state.removedPastSessions", "Past sessions removed from the sidebar",
                "The ones you took off with × or a group's menu. History lists them all the same.",
                keywords: ["hidden", "removed", "past sessions", "restore", "show again"]),
        ], description: "Work in the folders Claude Code already knows, straight from the sidebar.");
        LoadFavorites();
    }

    public override string IconKey => "IconSettingsTabs";

    public override string Description => "What every tab does at a usage limit, on a long turn and with its processes, and the folders you open them in.";

    // ---- At a usage limit (DESIGN.md §6) --------------------------------------------------------------------------

    /// <summary>DESIGN.md §6, "Continuing after a limit resets". Each tab can override it in Tab settings.</summary>
    public bool ContinueAfterLimitReset
    {
        get => Settings.Usage.ContinueAfterLimitReset;
        set => Set(value, v => Settings.Usage.ContinueAfterLimitReset = v);
    }

    /// <summary>DESIGN.md §6, "Tabs that don't use usage credits". Each tab can override it from its menu or Tab settings.</summary>
    public bool UseUsageCredits
    {
        get => Settings.Usage.UseUsageCredits;
        set => Set(value, v => Settings.Usage.UseUsageCredits = v);
    }

    // ---- Check-ins (DESIGN.md §5, "Check-ins on long turns") ---------------------------------------------------------

    public bool CheckInsEnabled
    {
        get => Settings.CheckIns.Enabled;
        set => Set(value, v => Settings.CheckIns.Enabled = v);
    }

    public decimal? CheckInRunTime
    {
        get => Settings.CheckIns.RunTimeMinutes;
        set => Set(value, v => Settings.CheckIns.RunTimeMinutes = Math.Max(0, (int)(v ?? 0)));
    }

    public decimal? CheckInQuietTime
    {
        get => Settings.CheckIns.QuietTimeMinutes;
        set => Set(value, v => Settings.CheckIns.QuietTimeMinutes = Math.Max(0, (int)(v ?? 0)));
    }

    public string CheckInMessage
    {
        get => Settings.CheckIns.Message;
        set => Set(value, v => Settings.CheckIns.Message = string.IsNullOrWhiteSpace(v) ? CheckInSettings.DefaultMessage : v);
    }

    public bool NotifyOnCheckIn
    {
        get => Settings.CheckIns.Notify;
        set => Set(value, v => Settings.CheckIns.Notify = v);
    }

    // ---- The process monitor (DESIGN.md §4, "Process monitor") ------------------------------------------------------

    public bool ShowProcessMonitor
    {
        get => Settings.Processes.ShowMonitor;
        set => Set(value, v => Settings.Processes.ShowMonitor = v);
    }

    // ---- Claude Code's projects (DESIGN.md §4, "Claude Code's projects") -----------------------------------------

    public bool ShowClaudeProjects
    {
        get => Settings.Sidebar.ShowClaudeProjects;
        set => Set(value, v => Settings.Sidebar.ShowClaudeProjects = v);
    }

    /// <summary>How many past sessions are off the sidebar, for the row's button.</summary>
    public int RemovedPastSessionCount => Services.State.RemovedPastSessions.Count;

    public bool HasRemovedPastSessions => RemovedPastSessionCount > 0;

    public string ShowRemovedPastSessionsText => HasRemovedPastSessions ? $"Show {RemovedPastSessionCount} again" : "None removed";

    /// <summary><b>Show them again</b>: the sidebar lists them once more (the shell follows the state).</summary>
    [RelayCommand]
    private void ShowRemovedPastSessions()
    {
        Services.State.RemovedPastSessions.Clear();
        Services.SaveState();
        OnRemovedPastSessionsChanged();
    }

    /// <summary>Counted again each time the page shows, since the sidebar removes them while the window stays open.</summary>
    public override void OnShown() => OnRemovedPastSessionsChanged();

    private void OnRemovedPastSessionsChanged()
    {
        OnPropertyChanged(nameof(RemovedPastSessionCount));
        OnPropertyChanged(nameof(HasRemovedPastSessions));
        OnPropertyChanged(nameof(ShowRemovedPastSessionsText));
    }

    public decimal? ProcessRefreshSeconds
    {
        get => Settings.Processes.RefreshSeconds;
        set => Set(value, v => Settings.Processes.RefreshSeconds = Math.Clamp((int)(v ?? 2), 1, 60));
    }

    public bool ShowCommandLines
    {
        get => Settings.Processes.ShowCommandLines;
        set => Set(value, v => Settings.Processes.ShowCommandLines = v);
    }

    // ---- Recent folders --------------------------------------------------------------------------------------------

    public decimal? RecentFolderLimit
    {
        get => Settings.NewTabs.RecentFolderLimit;
        set => Set(value, v => Settings.NewTabs.RecentFolderLimit = Math.Clamp((int)(v ?? 20), 1, 100));
    }

    [RelayCommand]
    private void ClearRecentFolders()
    {
        Services.State.RecentFolders.RemoveAll(r => !FolderHistory.IsFavorite(Services.State, r.Path));
        Services.SaveState();
    }

    // ---- Favorite folders (DESIGN.md §4, "Opening a tab") -----------------------------------------------------------

    public ObservableCollection<FavoriteFolderRow> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    private void LoadFavorites()
    {
        Favorites.Clear();
        foreach (var path in Services.State.FavoriteFolders)
        {
            Favorites.Add(new FavoriteFolderRow(path, Formats.FolderName(path)));
        }
        OnPropertyChanged(nameof(HasFavorites));
    }

    [RelayCommand]
    private async Task AddFavoriteAsync()
    {
        if (await Services.Platform.PickFolderAsync("Add a favorite folder") is { } folder)
        {
            FolderHistory.SetFavorite(Services.State, folder, true);
            Services.SaveState();
            LoadFavorites();
        }
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteFolderRow? row)
    {
        if (row is not null)
        {
            FolderHistory.SetFavorite(Services.State, row.Path, false);
            Services.SaveState();
            LoadFavorites();
        }
    }

    [RelayCommand]
    private void MoveFavoriteUp(FavoriteFolderRow? row) => MoveFavorite(row, -1);

    [RelayCommand]
    private void MoveFavoriteDown(FavoriteFolderRow? row) => MoveFavorite(row, 1);

    private void MoveFavorite(FavoriteFolderRow? row, int by)
    {
        var favorites = Services.State.FavoriteFolders;
        var index = row is null ? -1 : favorites.IndexOf(row.Path);
        var target = index + by;
        if (index < 0 || target < 0 || target >= favorites.Count)
        {
            return;
        }
        (favorites[index], favorites[target]) = (favorites[target], favorites[index]);
        Services.SaveState();
        LoadFavorites();
    }
}
