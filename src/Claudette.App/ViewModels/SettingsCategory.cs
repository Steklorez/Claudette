namespace Claudette.App.ViewModels;

/// <summary>
/// The Settings window's categories, by the names its sidebar shows (DESIGN.md §14). Use these rather than the names
/// written out, so a page, its search entries and a link to it can't disagree. The selected tab's project pages are
/// named by the project instead (<see cref="SettingsViewModel.ProjectPages"/>).
/// </summary>
public static class SettingsCategory
{
    public const string General = "General";

    public const string Appearance = "Appearance";

    public const string Conversation = "Conversation";

    public const string Notifications = "Notifications";

    public const string Keyboard = "Keyboard";

    public const string ClaudeCode = "Claude Code";

    public const string Accounts = "Accounts";

    public const string Tabs = "Tabs";

    public const string Usage = "Usage";

    public const string Sessions = "Sessions";

    public const string Files = "Files and diffs";

    public const string ProjectTools = "Project tools";

    public const string Perforce = "Perforce";

    public const string Advanced = "Advanced";
}

/// <summary>
/// The Settings sidebar's groups (DESIGN.md §14), in order: a heading and its categories. The last has no heading.
/// </summary>
public static class SettingsSidebar
{
    public static IReadOnlyList<(string? Heading, IReadOnlyList<string> Categories)> Groups { get; } =
    [
        ("Claudette", [SettingsCategory.General, SettingsCategory.Appearance, SettingsCategory.Conversation, SettingsCategory.Notifications, SettingsCategory.Keyboard]),
        ("Claude", [SettingsCategory.ClaudeCode, SettingsCategory.Accounts, SettingsCategory.Tabs, SettingsCategory.Usage]),
        ("Your work", [SettingsCategory.Sessions, SettingsCategory.Files, SettingsCategory.ProjectTools, SettingsCategory.Perforce]),
        (null, [SettingsCategory.Advanced]),
    ];
}

/// <summary>An entry in the Settings sidebar: a group's heading, or a page to show.</summary>
/// <param name="IconKey">A page's icon: the key of one of App.axaml's small vector icons.</param>
public sealed record SettingsNavEntry(string Title, string? IconKey, bool IsHeading)
{
    /// <summary>The page's name, for a heading's group or a page; what <see cref="SettingsViewModel.SelectedCategory"/> holds.</summary>
    public string Category => Title;

    /// <summary>A heading's tooltip, such as the project's folder.</summary>
    public string? Tip { get; init; }

    public static SettingsNavEntry Heading(string title, string? tip = null) => new(title, null, true) { Tip = tip };

    /// <summary>What a screen reader reads for it: its name.</summary>
    public override string ToString() => Title;

    public static SettingsNavEntry Page(string title, string iconKey) => new(title, iconKey, false);
}
