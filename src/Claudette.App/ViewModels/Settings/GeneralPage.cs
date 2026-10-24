using Claudette.Core.LoginItems;
using Claudette.Core.Settings;
using Claudette.Core.Updates;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>A choice for <b>Remove worktrees no tab has used for</b>.</summary>
public sealed record InactiveDaysChoice(int Days)
{
    public override string ToString() => Days == 0 ? "Never" : $"{Days} days";
}

/// <summary>
/// Settings → General (DESIGN.md §14): starting at login and restoring tabs, closing a working tab, keeping the computer
/// awake, cleaning up worktrees, Claude's service status and Claudette's own updates. Dispose it with the window.
/// </summary>
public sealed partial class GeneralPage : SettingsPage, IDisposable
{
    private LoginItemStatus? _loginItem;
    private bool _changingLoginItem;

    public GeneralPage(SettingsContext context) : base(context, SettingsCategory.General)
    {
        Section("starting", "Starting and closing",
        [
            // The OS's entry, not settings.json: never reset, and never synced.
            Setting("state.startAtLogin", "Start Claudette when I log in", null, null, keywords: ["login item", "startup", "launch at login"], machineOnly: true),
            Setting("sessions.restoreUnpinnedTabs", "Also restore unpinned tabs when Claudette starts", s => s.Sessions.RestoreUnpinnedTabs, (s, v) => s.Sessions.RestoreUnpinnedTabs = v,
                "Pinned tabs always come back.", keywords: ["reopen", "session restore"]),
            Setting("general.confirmCloseWorkingTab", "Confirm before closing a tab where Claude is working",
                s => s.General.ConfirmCloseWorkingTab, (s, v) => s.General.ConfirmCloseWorkingTab = v,
                "Closing one stops Claude mid-turn."),
        ]);
        Section("keepAwake", "Keeping the computer awake",
        [
            Setting("general.keepAwake", "Keep this computer awake", s => s.General.KeepAwake, (s, v) => s.General.KeepAwake = v,
                "So a task a usage limit stopped can carry on by itself when the limit resets.",
                "Only sleep is held off: the screen can still turn off, and the screensaver can still start. Closing a laptop's lid may still put it to sleep. The cup in the header turns this on and off, and steams while the computer is kept awake.",
                ["sleep", "caffeinate", "cup", "insomnia"]),
            Setting("general.keepAwakeWhen", "When", s => s.General.KeepAwakeWhen, (s, v) => s.General.KeepAwakeWhen = v,
                more: "Claude has work to finish while a turn runs, while Claude Code keeps tasks running in the background, or while a task waits for a usage limit to reset so it can carry on. Once there's none, the computer can sleep again a couple of minutes later."),
            Setting("claudeCode.keepAwakeWhileConnected", "Also while tabs are connected to the Claude app",
                s => s.ClaudeCode.KeepAwakeWhileConnected, (s, v) => s.ClaudeCode.KeepAwakeWhileConnected = v,
                "So the Claude app can still reach them. The screen can still turn off.", keywords: ["remote control", "phone"]),
        ]);
        Section("worktrees", "Worktrees",
        [
            Setting("general.removeMergedWorktrees", "Remove worktrees once everything in them is merged",
                s => s.General.RemoveMergedWorktrees, (s, v) => s.General.RemoveMergedWorktrees = v,
                "Every commit is on another branch, so nothing is lost.",
                "The worktree and its branch go a day after a tab last used it. One with uncommitted changes is always kept, and files git ignores there, such as build output, go with it.",
                ["clean up", "git"]),
            Setting("general.removeInactiveWorktreesAfterDays", "Remove worktrees no tab has used for",
                s => s.General.RemoveInactiveWorktreesAfterDays, (s, v) => s.General.RemoveInactiveWorktreesAfterDays = v,
                "Its branch stays when it has commits on no other branch.",
                "So a new worktree tab can carry on from the branch. One with uncommitted changes is always kept.",
                ["clean up", "stale", "git"]),
            Info("general.cleanUpWorktrees", "Last cleanup", keywords: ["clean up now", "worktrees"]),
        ], description: "The worktrees of worktree tabs that no open tab works in.");
        Section("updates", "Updates and status",
        [
            Setting("general.showServiceStatus", "Show Claude's service status", s => s.General.ShowServiceStatus, (s, v) => s.General.ShowServiceStatus = v,
                "A dot in the header, and a banner while Claude has an incident.",
                "Read from status.claude.com every 5 minutes: Claude Code, the Claude API and claude.ai. Off, it isn't checked at all.",
                ["outage", "incident", "status page"]),
            Setting("general.checkForAppUpdates", "Check for Claudette updates automatically", s => s.General.CheckForAppUpdates, (s, v) => s.General.CheckForAppUpdates = v,
                "From Claudette's releases on GitHub, at launch and every few hours.", keywords: ["version", "upgrade"]),
            Setting("general.includePrereleases", "Include pre-releases", s => s.General.IncludePrereleases, (s, v) => s.General.IncludePrereleases = v,
                "Try new versions before they're released.", keywords: ["beta", "preview"]),
            Info("general.version", "Claudette version", keywords: ["update now", "check now", "release notes"]),
        ]);
        _ = RefreshStartAtLoginAsync();
        Services.KeepAwake.PropertyChanged += OnKeepAwakeChanged;
    }

    public override string IconKey => "IconSettings";

    public override string Description => "Starting Claudette, keeping the computer awake, worktrees and updates.";

    // ---- Starting at login (DESIGN.md §9, "Starting at login") ----------------------------------------------------

    /// <summary>
    /// Reads whether Claudette starts at login, as the window opens: Task Manager, System Settings or another copy of
    /// Claudette may have changed it since (DESIGN.md §9, "Starting at login").
    /// </summary>
    public async Task RefreshStartAtLoginAsync() => UseLoginItem(await Services.StartAtLogin.ReadAsync());

    /// <summary>
    /// <b>Start Claudette when I log in</b>. It's this machine's, and lives in the OS rather than in settings.json, so
    /// Reset to defaults leaves it.
    /// </summary>
    public bool StartsAtLogin
    {
        get => _loginItem?.IsOn == true;
        set
        {
            if (value != StartsAtLogin && CanChangeStartAtLogin)
            {
                _ = SetStartAtLoginAsync(value);
            }
        }
    }

    public bool CanChangeStartAtLogin => _loginItem?.CanChange == true && !_changingLoginItem;

    /// <summary>What happens at login, and which copy of Claudette starts when it isn't simply this one.</summary>
    public string StartAtLoginText
    {
        get
        {
            const string opens = "Claudette opens minimized when you log in to this computer.";
            var current = Services.ThisCopy;
            return _loginItem?.Starts switch
            {
                { } starts when !starts.IsSameCopy(current) => $"{opens} It starts {starts.Describe()} rather than this {current.KindName}.",
                { Kind: AppInstallKind.SourceBuild } => $"{opens} It starts this source build's newest build. Once Claudette is installed, the installed one starts instead.",
                _ => opens,
            };
        }
    }

    /// <summary>Why the switch can't be changed here, such as being turned off in Task Manager.</summary>
    public string? StartAtLoginNote => _loginItem?.Note;

    public bool HasStartAtLoginNote => StartAtLoginNote is not null;

    public string? StartAtLoginError => _loginItem?.Error;

    public bool HasStartAtLoginError => StartAtLoginError is not null;

    private async Task SetStartAtLoginAsync(bool on)
    {
        _changingLoginItem = true;
        OnPropertyChanged(nameof(CanChangeStartAtLogin));
        try
        {
            UseLoginItem(await Services.StartAtLogin.SetAsync(on));
        }
        finally
        {
            _changingLoginItem = false;
            OnPropertyChanged(nameof(CanChangeStartAtLogin));
        }
    }

    /// <summary>What the OS says now: everything the switch and its notes show follows it.</summary>
    private void UseLoginItem(LoginItemStatus status)
    {
        _loginItem = status;
        OnPropertyChanged(string.Empty);
    }

    // ---- Tabs ----------------------------------------------------------------------------------------------------------

    public bool RestoreUnpinnedTabs
    {
        get => Settings.Sessions.RestoreUnpinnedTabs;
        set => Set(value, v => Settings.Sessions.RestoreUnpinnedTabs = v);
    }

    public bool ConfirmCloseWorkingTab
    {
        get => Settings.General.ConfirmCloseWorkingTab;
        set => Set(value, v => Settings.General.ConfirmCloseWorkingTab = v);
    }

    /// <summary>
    /// The header's dot and the banner, from status.claude.com (DESIGN.md §18, "Service status"). Off stops the checks.
    /// </summary>
    public bool ShowServiceStatus
    {
        get => Settings.General.ShowServiceStatus;
        set => Set(value, v => Settings.General.ShowServiceStatus = v);
    }

    // ---- Keeping the computer awake (DESIGN.md §9, "Keeping the computer awake") ----------------------------------

    /// <summary><b>Keep this computer awake</b>: the same switch as the header's cup.</summary>
    public bool KeepAwake
    {
        get => Settings.General.KeepAwake;
        set => Set(value, v => Settings.General.KeepAwake = v);
    }

    /// <summary>Only while Claude has work to finish: a turn, background tasks, or a task waiting for a limit to reset.</summary>
    public bool KeepAwakeWhileClaudeHasWork
    {
        get => Settings.General.KeepAwakeWhen == KeepAwakeWhen.ClaudeHasWork;
        set
        {
            if (value)
            {
                SetKeepAwakeWhen(KeepAwakeWhen.ClaudeHasWork);
            }
        }
    }

    /// <summary>Whenever Claudette is running.</summary>
    public bool KeepAwakeWhileClaudetteRuns
    {
        get => Settings.General.KeepAwakeWhen == KeepAwakeWhen.ClaudetteRuns;
        set
        {
            if (value)
            {
                SetKeepAwakeWhen(KeepAwakeWhen.ClaudetteRuns);
            }
        }
    }

    private void SetKeepAwakeWhen(KeepAwakeWhen when)
    {
        if (Settings.General.KeepAwakeWhen == when)
        {
            return;
        }
        Settings.General.KeepAwakeWhen = when;
        OnPropertyChanged(nameof(KeepAwakeWhileClaudeHasWork));
        OnPropertyChanged(nameof(KeepAwakeWhileClaudetteRuns));
        Save();
    }

    /// <summary><b>Also while tabs are connected to the Claude app</b>: system sleep only; the display can still sleep.</summary>
    public bool KeepAwakeWhileConnected
    {
        get => Settings.ClaudeCode.KeepAwakeWhileConnected;
        set => Set(value, v => Settings.ClaudeCode.KeepAwakeWhileConnected = v);
    }

    /// <summary>Why this computer can't be kept awake, or null when it can.</summary>
    public string? KeepAwakeUnavailableText => Services.KeepAwake.IsAvailable ? null : Services.KeepAwake.Tooltip;

    /// <summary>The header's cup changed the switch while the window is open: the page follows.</summary>
    private void OnKeepAwakeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(KeepAwake));
        OnPropertyChanged(nameof(KeepAwakeWhileClaudeHasWork));
        OnPropertyChanged(nameof(KeepAwakeWhileClaudetteRuns));
    }

    public void Dispose() => Services.KeepAwake.PropertyChanged -= OnKeepAwakeChanged;

    // ---- Cleaning up worktrees (DESIGN.md §4, "Cleaning up worktrees") ---------------------------------------------

    public bool RemoveMergedWorktrees
    {
        get => Settings.General.RemoveMergedWorktrees;
        set
        {
            Set(value, v => Settings.General.RemoveMergedWorktrees = v);
            OnPropertyChanged(nameof(CanCleanUpWorktrees));
        }
    }

    /// <summary>The choices for <b>Remove worktrees no tab has used for</b>; 0 is never.</summary>
    public IReadOnlyList<InactiveDaysChoice> InactiveDaysChoices { get; } = [.. new[] { 0, 7, 14, 30, 90 }.Select(d => new InactiveDaysChoice(d))];

    public InactiveDaysChoice RemoveInactiveWorktreesAfter
    {
        get => InactiveDaysChoices.FirstOrDefault(c => c.Days == Settings.General.RemoveInactiveWorktreesAfterDays)
            ?? new InactiveDaysChoice(Settings.General.RemoveInactiveWorktreesAfterDays);
        set
        {
            if (value is not null && value.Days != Settings.General.RemoveInactiveWorktreesAfterDays)
            {
                Set(value.Days, v => Settings.General.RemoveInactiveWorktreesAfterDays = v);
                OnPropertyChanged(nameof(CanCleanUpWorktrees));
            }
        }
    }

    /// <summary>What the last cleanup removed, and when: "Last removed brisk-otter and calm-heron, 2 h ago."</summary>
    public string WorktreeCleanupText
    {
        get
        {
            if (_cleanupResult is { } result)
            {
                return result;
            }
            if (Services.State.LastWorktreeCleanup is not { Removed.Count: > 0 } last)
            {
                return "Nothing removed yet. Claudette looks a minute after it starts, then every hour.";
            }
            var names = last.Removed.Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))).ToList();
            return $"Last removed {Core.Formats.List(names)}, {Core.Formats.Ago(Services.Time.GetUtcNow() - last.At)}.";
        }
    }

    public bool CanCleanUpWorktrees => (RemoveMergedWorktrees || Settings.General.RemoveInactiveWorktreesAfterDays > 0) && !_cleaningUp;

    private string? _cleanupResult;
    private bool _cleaningUp;

    /// <summary><b>Clean up now</b>: a pass straight away, rather than at the next hour.</summary>
    [RelayCommand]
    private async Task CleanUpWorktreesAsync()
    {
        _cleaningUp = true;
        OnPropertyChanged(nameof(CanCleanUpWorktrees));
        try
        {
            var removed = await Services.Worktrees.RunAsync();
            _cleanupResult = removed.Count switch
            {
                0 => "Nothing to remove right now.",
                1 => $"Removed {Path.GetFileName(removed[0])}.",
                _ => $"Removed {removed.Count} worktrees: {string.Join(", ", removed.Select(Path.GetFileName))}.",
            };
        }
        finally
        {
            _cleaningUp = false;
            OnPropertyChanged(nameof(CanCleanUpWorktrees));
            OnPropertyChanged(nameof(WorktreeCleanupText));
        }
    }

    // ---- Claudette's own updates (DESIGN.md §2, "Updating Claudette") ---------------------------------------------

    /// <summary>Check GitHub for new Claudette releases.</summary>
    public bool CheckForAppUpdates
    {
        get => Settings.General.CheckForAppUpdates;
        set => Set(value, v => Settings.General.CheckForAppUpdates = v);
    }

    public bool IncludePrereleases
    {
        get => Settings.General.IncludePrereleases;
        set
        {
            Set(value, v => Settings.General.IncludePrereleases = v);
            // Takes effect with the next check: do that now.
            AppUpdates?.CheckNowCommand.Execute(null);
        }
    }

    /// <summary>Claudette's version, update status and actions, the same as the sidebar's badge. Null in some tests.</summary>
    public AppUpdateViewModel? AppUpdates { get; internal set; }

    public bool HasAppUpdates => AppUpdates is not null && SourceUpdates is null;

    /// <summary>For a source build: its checkout's new commits upstream, in place of releases (DESIGN.md §9, "New commits upstream").</summary>
    public SourceUpdateViewModel? SourceUpdates { get; internal set; }

    public bool HasSourceUpdates => SourceUpdates is not null;

    /// <summary>A copy from the Microsoft Store is the Store's to update, so the release settings don't apply to it.</summary>
    public bool ShowsUpdateChecks => AppUpdates?.IsFromStore != true;

    /// <summary>Pre-releases are GitHub's: not for a source build, which follows its checkout, or a copy from the Store.</summary>
    public bool ShowsPrereleases => ShowsUpdateChecks && !HasSourceUpdates;
}
