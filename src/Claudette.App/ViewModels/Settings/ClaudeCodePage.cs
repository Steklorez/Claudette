using Claudette.Core.Installation;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// Settings → Claude Code (DESIGN.md §14): the installed version and its updates, the path to it, the account, how a
/// session starts (its name, folder trust, the login shell's environment, checkpoints, the task list, the fallback model),
/// and the Claude app. Dispose it with the window.
/// </summary>
public sealed partial class ClaudeCodePage : SettingsPage, IDisposable
{
    private readonly TabsPage _tabs;

    /// <param name="tabs">Tabs, whose model list <b>Fallback model</b> offers too.</param>
    public ClaudeCodePage(SettingsContext context, string? accountText, ClaudeUpdateViewModel? updates, TabsPage tabs) : base(context, SettingsCategory.ClaudeCode)
    {
        _tabs = tabs;
        AccountText = accountText ?? "Not signed in";
        Updates = updates;
        Section("installed", "Claude Code",
        [
            Info("claudeCode.version", "Installed version", keywords: ["installed with", "update now", "claude doctor", "what's new"]),
            Setting("claudeCode.checkForUpdates", "Check for Claude Code updates automatically", s => s.ClaudeCode.CheckForUpdates, (s, v) => s.ClaudeCode.CheckForUpdates = v,
                "At launch and every few hours. Open tabs keep the version they started with.", keywords: ["update claude code", "version"]),
            Setting("claudeCode.claudePath", "Path to claude", s => s.ClaudeCode.ClaudePath, (s, v) => s.ClaudeCode.ClaudePath = v,
                "Empty finds Claude Code by itself. A change takes effect the next time Claudette starts.", keywords: ["executable", "binary", "location"]),
        ]);
        Section("account", "Account",
        [
            Info("claudeCode.account", "Signed-in account", keywords: ["sign in", "sign out", "login", "logout", "email", "plan"]),
        ]);
        Section("sessions", "Starting a session",
        [
            Setting("general.renameInClaudeCode", "Name each tab's session in Claude Code after the tab", s => s.General.RenameInClaudeCode, (s, v) => s.General.RenameInClaudeCode = v,
                "So other sessions, and claude --resume <name> in a terminal, find it by name.",
                "Otherwise Claude Code names it after its folder, such as claudette-3f.", ["session name", "resume"]),
            Setting("claudeCode.askBeforeUsingFolderSettings", "Ask before using a new folder's own configuration", s => s.ClaudeCode.AskBeforeUsingFolderSettings,
                (s, v) => s.ClaudeCode.AskBeforeUsingFolderSettings = v,
                "Before Claude Code first starts in a folder you haven't trusted, see what it would run.",
                "Lists the hooks, commands and MCP servers the folder's configuration would run. Started by Claudette, Claude Code doesn't ask itself.",
                ["folder trust", "hooks", "security"]),
            .. ShowLoginShellSetting
                ? [Setting("claudeCode.useLoginShellEnvironment", "Use my login shell's environment", s => s.ClaudeCode.UseLoginShellEnvironment, (s, v) => s.ClaudeCode.UseLoginShellEnvironment = v,
                    "Claude and the tools Claudette runs get the PATH your shell profile sets up, as in a terminal.",
                    "And the other variables it sets. Not needed when Claudette is started from a terminal.", ["path", "zshrc", "bashrc", "environment variables"])]
                : Array.Empty<SettingItem>(),
            Setting("claudeCode.keepFileCheckpoints", "Keep copies of files Claude changes, so prompts can be rewound", s => s.ClaudeCode.KeepFileCheckpoints,
                (s, v) => s.ClaudeCode.KeepFileCheckpoints = v,
                "Restore files and Edit and resend put files back as they were before a prompt.",
                "Claude Code keeps the copies with the session, as its terminal does.", ["checkpoints", "rewind", "undo"]),
            Setting("claudeCode.giveClaudeTaskList", "Give Claude a task list", s => s.ClaudeCode.GiveClaudeTaskList, (s, v) => s.ClaudeCode.GiveClaudeTaskList = v,
                "Claude writes down its steps and ticks them off, so you can see how far it's got.",
                "On the to-do list over the conversation, the status line and the tab's row. Claude Code leaves the list out for newer models, to save room in the context.",
                ["todo", "tasks", "plan"]),
            Setting("claudeCode.subagentProgressSummaries", "Summarize subagents' progress", s => s.ClaudeCode.SubagentProgressSummaries,
                (s, v) => s.ClaudeCode.SubagentProgressSummaries = v,
                "A line about what each subagent has got to, for the Agents page.",
                "Each summary is a small extra model call, which counts toward your usage.", ["agents", "agent map"]),
            Setting("claudeCode.fallbackModel", "Fallback model", s => s.ClaudeCode.FallbackModel, (s, v) => s.ClaudeCode.FallbackModel = v,
                "When a tab's model is overloaded, Claude Code switches to this one rather than wait.", keywords: ["overloaded", "529"]),
        ], description: "These apply to tabs started after a change.");
        Section("claudeApp", "Claude app",
        [
            Setting("claudeCode.connectNewTabsToClaudeApp", "Connect new tabs to the Claude app", s => s.ClaudeCode.ConnectNewTabsToClaudeApp,
                (s, v) => s.ClaudeCode.ConnectNewTabsToClaudeApp = v,
                "Continue a tab from your phone or claude.ai/code while it runs here.",
                "Each tab has Connect to the Claude app in its menu; this sets it for tabs opened from now on. While a tab is connected, its transcript is stored on Anthropic's servers. Needs a claude.ai subscription sign-in. General keeps the computer awake while tabs are connected.",
                ["remote control", "phone", "mobile"]),
            Info("claudeCode.pushNotifications", "Push notifications on your phone",
                "Turn them on in Claude Code's /config: none arrive while Claudette is in front.",
                "Push when actions required sends one when a tab needs an answer; Push when Claude decides adds the ones Claude chooses to send.",
                ["mobile", "remote control"]),
        ], description: "Remote Control: a tab here, continued from the Claude app.");
        // Signing in from this window can make the Claude app available, or not (DESIGN.md §18).
        Services.RemoteControl.AvailabilityChanged += OnRemoteControlAvailabilityChanged;
    }

    public void Dispose() => Services.RemoteControl.AvailabilityChanged -= OnRemoteControlAvailabilityChanged;

    public override string IconKey => "IconCardClaude";

    public override string Description => "The Claude Code Claudette runs, your account, and how each session starts.";

    // ---- The installed version and the account ----------------------------------------------------------------------

    public string AccountText { get; }

    /// <summary>The account, with <b>Sign in</b> and <b>Sign out</b> wired to the header's (DESIGN.md §11). Null in some tests.</summary>
    public AccountViewModel? Account { get; internal set; }

    public bool HasAccount => Account is not null;

    /// <summary>Version, install method, update checks and <b>Update now</b> (DESIGN.md §12, §14). Null before Claude Code is found.</summary>
    public ClaudeUpdateViewModel? Updates { get; }

    public bool HasUpdates => Updates is not null;

    public bool CheckForUpdates
    {
        get => Settings.ClaudeCode.CheckForUpdates;
        set => Set(value, v => Settings.ClaudeCode.CheckForUpdates = v);
    }

    public string InstalledText => Services.Install is { } install
        ? $"Claude Code {install.Version} at {install.Path}. Last tested with {ClaudeLocator.LastTestedVersion}"
            + (install.Version > ClaudeLocator.LastTestedVersion ? " (this version is newer)."
                : install.Version < ClaudeLocator.LastTestedVersion ? " (this version is older; updating is recommended)."
                : ".")
        : "Claude Code wasn't found.";

    /// <summary>Null (empty) finds Claude Code automatically. Takes effect the next time Claudette starts.</summary>
    public string ClaudePath
    {
        get => Settings.ClaudeCode.ClaudePath ?? "";
        set => Set(value, v => Settings.ClaudeCode.ClaudePath = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    [RelayCommand]
    private async Task BrowseClaudePathAsync()
    {
        if (await Services.Platform.PickFileAsync("Choose the claude executable") is { } path)
        {
            ClaudePath = path;
            OnPropertyChanged(nameof(ClaudePath));
        }
    }

    // ---- Starting a session (DESIGN.md §5, "Rewind and branch"; §7, "Folder trust"; §13) ---------------------------

    public bool RenameInClaudeCode
    {
        get => Settings.General.RenameInClaudeCode;
        set => Set(value, v => Settings.General.RenameInClaudeCode = v);
    }

    /// <summary><b>Fallback model</b>: none, or one of the models Tabs offers.</summary>
    public IReadOnlyList<Choice> FallbackModelChoices => field ??=
        [new(null, "None"), .. _tabs.ModelChoices.Where(c => c.Value is not null)];

    public Choice FallbackModel
    {
        get => FallbackModelChoices.FirstOrDefault(c => c.Value == Settings.ClaudeCode.FallbackModel)
            ?? (Settings.ClaudeCode.FallbackModel is { } custom ? new Choice(custom, custom) : FallbackModelChoices[0]);
        set => Set(value?.Value, v => Settings.ClaudeCode.FallbackModel = v);
    }

    /// <summary><b>Keep copies of files Claude changes, so prompts can be rewound</b>.</summary>
    public bool KeepFileCheckpoints
    {
        get => Settings.ClaudeCode.KeepFileCheckpoints;
        set => Set(value, v => Settings.ClaudeCode.KeepFileCheckpoints = v);
    }

    /// <summary><b>Give Claude a task list</b> (DESIGN.md §5, "Tasks").</summary>
    public bool GiveClaudeTaskList
    {
        get => Settings.ClaudeCode.GiveClaudeTaskList;
        set => Set(value, v => Settings.ClaudeCode.GiveClaudeTaskList = v);
    }

    /// <summary><b>Summarize subagents' progress</b> (DESIGN.md §18, "Agent map").</summary>
    public bool SubagentProgressSummaries
    {
        get => Settings.ClaudeCode.SubagentProgressSummaries;
        set => Set(value, v => Settings.ClaudeCode.SubagentProgressSummaries = v);
    }

    /// <summary><b>Ask before using a new folder's own configuration</b> (DESIGN.md §7, "Folder trust").</summary>
    public bool AskBeforeUsingFolderSettings
    {
        get => Settings.ClaudeCode.AskBeforeUsingFolderSettings;
        set => Set(value, v => Settings.ClaudeCode.AskBeforeUsingFolderSettings = v);
    }

    /// <summary>
    /// <b>Use my login shell's environment</b> (DESIGN.md §13, "Login shell environment"). Turning it on reads the login
    /// shell then, if this run hasn't yet; either way it applies to processes started after.
    /// </summary>
    public bool UseLoginShellEnvironment
    {
        get => Settings.ClaudeCode.UseLoginShellEnvironment;
        set => Set(value, v => Settings.ClaudeCode.UseLoginShellEnvironment = v);
    }

    /// <summary>Only macOS and Linux have the setting: on Windows, apps get the user's full environment.</summary>
    public static bool ShowLoginShellSetting => !OperatingSystem.IsWindows();

    // ---- The Claude app (DESIGN.md §18, "Remote Control") --------------------------------------------------------

    /// <summary>The docs' steps for pushes to the phone. Claudette doesn't change Claude Code's settings for them.</summary>
    public const string PushNotificationsDocs = "https://code.claude.com/docs/en/remote-control#mobile-push-notifications";

    /// <summary>
    /// <b>Connect new tabs to the Claude app</b>: the switch new tabs start with, like <b>Sync new tabs</b>. Open tabs
    /// keep theirs; each has <b>Connect to the Claude app</b> in its menu.
    /// </summary>
    public bool ConnectNewTabsToClaudeApp
    {
        get => Settings.ClaudeCode.ConnectNewTabsToClaudeApp;
        set => Set(value, v => Settings.ClaudeCode.ConnectNewTabsToClaudeApp = v);
    }

    /// <summary>The account can use Remote Control. When it can't, the setting and the tabs' switches are disabled.</summary>
    public bool CanUseRemoteControl => Services.RemoteControl.IsAvailable;

    /// <summary>Why the account can't use Remote Control, from <c>claude auth status</c> and the environment.</summary>
    public string? RemoteControlUnavailableText => Services.RemoteControl.UnavailableReason is { } reason ? $"Not available: {reason}" : null;

    public bool HasRemoteControlUnavailableText => RemoteControlUnavailableText is not null;

    [RelayCommand]
    private Task OpenPushNotificationsDocsAsync() => Services.Platform.OpenUrlAsync(PushNotificationsDocs);

    private void OnRemoteControlAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanUseRemoteControl));
        OnPropertyChanged(nameof(RemoteControlUnavailableText));
        OnPropertyChanged(nameof(HasRemoteControlUnavailableText));
    }
}
