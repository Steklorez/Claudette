namespace Claudette.App.ViewModels.Settings;

/// <summary>Settings → Notifications (DESIGN.md §10): each kind on or off, the Dock or taskbar badge and the icon.</summary>
public sealed class NotificationsPage : SettingsPage
{
    public NotificationsPage(SettingsContext context) : base(context, SettingsCategory.Notifications)
    {
        Section("notify", "Notify me when",
        [
            Setting("notifications.turnFinished", "A tab finishes its turn", s => s.Notifications.TurnFinished, (s, v) => s.Notifications.TurnFinished = v,
                keywords: ["done", "complete"]),
            Setting("notifications.needsInput", "A tab needs permission or an answer", s => s.Notifications.NeedsInput, (s, v) => s.Notifications.NeedsInput = v,
                keywords: ["prompt", "question", "waiting"]),
            Setting("notifications.processErrors", "A tab's Claude Code stops with an error", s => s.Notifications.ProcessErrors, (s, v) => s.Notifications.ProcessErrors = v,
                keywords: ["crash"]),
            Setting("notifications.usageAlerts", "Usage crosses a warning level, is on pace to run out, or resets", s => s.Notifications.UsageAlerts, (s, v) => s.Notifications.UsageAlerts = v,
                keywords: ["usage alerts", "limit"]),
            Setting("notifications.signIn", "Claude Code needs me to sign in", s => s.Notifications.SignIn, (s, v) => s.Notifications.SignIn = v,
                keywords: ["login", "account"]),
            Setting("notifications.updateReady", "A Claude Code update is ready", s => s.Notifications.UpdateReady, (s, v) => s.Notifications.UpdateReady = v,
                keywords: ["version"]),
            Setting("notifications.projectActions", "A project action finishes", s => s.Notifications.ProjectActions, (s, v) => s.Notifications.ProjectActions = v,
                keywords: ["build", "job", "run"]),
        ], description: "Only when Claudette isn't in front. Clicking one brings it to the front, on that tab. Check-ins have their own, in Tabs.");
        Section("icon", "The Dock or taskbar icon",
        [
            Setting("notifications.badge", "Show how many tabs need you", s => s.Notifications.Badge, (s, v) => s.Notifications.Badge = v,
                "A number on the icon.", keywords: ["badge", "count", "dock", "taskbar"]),
            Setting("notifications.animateIcon", "Animate it while tabs work or wait", s => s.Notifications.AnimateIcon, (s, v) => s.Notifications.AnimateIcon = v,
                "With motion reduced, it shows what the tabs are doing without moving.", keywords: ["dock", "taskbar", "animation"]),
        ]);
    }

    public override string IconKey => "IconSettingsNotifications";

    public override string Description => "When Claudette tells you something needs you.";

    /// <summary>False when this machine can't show OS notifications, for example a macOS build run outside its app bundle.</summary>
    public bool NotificationsAvailable => Services.Notifications.IsAvailable;

    public bool NotifyTurnFinished
    {
        get => Settings.Notifications.TurnFinished;
        set => Set(value, v => Settings.Notifications.TurnFinished = v);
    }

    public bool NotifyNeedsInput
    {
        get => Settings.Notifications.NeedsInput;
        set => Set(value, v => Settings.Notifications.NeedsInput = v);
    }

    public bool NotifyProcessErrors
    {
        get => Settings.Notifications.ProcessErrors;
        set => Set(value, v => Settings.Notifications.ProcessErrors = v);
    }

    public bool NotifyUsageAlerts
    {
        get => Settings.Notifications.UsageAlerts;
        set => Set(value, v => Settings.Notifications.UsageAlerts = v);
    }

    public bool NotifySignIn
    {
        get => Settings.Notifications.SignIn;
        set => Set(value, v => Settings.Notifications.SignIn = v);
    }

    public bool NotifyUpdateReady
    {
        get => Settings.Notifications.UpdateReady;
        set => Set(value, v => Settings.Notifications.UpdateReady = v);
    }

    /// <summary><b>A project action finishes</b> (DESIGN.md §18, "Project tools").</summary>
    public bool NotifyProjectActions
    {
        get => Settings.Notifications.ProjectActions;
        set => Set(value, v => Settings.Notifications.ProjectActions = v);
    }

    public bool ShowBadge
    {
        get => Settings.Notifications.Badge;
        set => Set(value, v => Settings.Notifications.Badge = v);
    }

    public bool AnimateIcon
    {
        get => Settings.Notifications.AnimateIcon;
        set => Set(value, v => Settings.Notifications.AnimateIcon = v);
    }
}
