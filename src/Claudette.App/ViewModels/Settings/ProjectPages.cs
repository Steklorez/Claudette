namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// One of the selected tab's project pages, below the categories (DESIGN.md §14, "The project's pages"). The pages
/// show <see cref="Project"/>, which holds what they edit; their search entries are named with the project's group
/// ("NightOwl → Links"), worked out as the search runs, since the project can be found after the window opens. Nothing
/// on them is a setting with a default: they have no <b>Reset to defaults</b>.
/// </summary>
public abstract class ProjectPage(SettingsContext context, string title, ProjectSettingsViewModel project) : SettingsPage(context, title)
{
    public ProjectSettingsViewModel Project { get; } = project;

    protected override SettingsSearchResult Entry(string key, string label, string? description, string? section, IEnumerable<string> words) =>
        base.Entry(key, label, description, section, words) with { Group = Project.Heading };
}

/// <summary>The project's <b>Links</b> (DESIGN.md §18, "Links"), from both of its files.</summary>
public sealed class ProjectLinksPage : ProjectPage
{
    public ProjectLinksPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, SettingsViewModel.LinksPage, project) =>
        Section("links", "Links",
        [
            Info("project.links", "Web links for this project", keywords: ["add a link", "url", "bookmark", "claudette.json"]),
        ]);

    public override string IconKey => "IconSettingsLinks";

    public override string Description => "Web links in the project's menu in the sidebar; a click opens one in the browser.";
}

/// <summary>The project's <b>Actions</b> (DESIGN.md §18, "Custom actions"), the editor that was in Tab settings….</summary>
public sealed class ProjectActionsPage : ProjectPage
{
    public ProjectActionsPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, SettingsViewModel.ActionsPage, project) =>
        Section("actions", "Actions",
        [
            Info("project.actions", "Commands of your own", keywords: ["add an action", "project actions", "claudette.json", "command"]),
        ]);

    public override string IconKey => "IconSettingsActions";

    public override string Description => "Commands of your own for this folder, in the project's menu in the sidebar.";
}

/// <summary>
/// The project's <b>Tools</b> page: this machine's choices for it, as the project menus make them (DESIGN.md §18). Its
/// entries depend on the project's kind: "Engine folder", "Editor configuration".
/// </summary>
public sealed class ProjectToolChoicesPage : ProjectPage
{
    public ProjectToolChoicesPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, SettingsViewModel.ToolsPage, project) =>
        Section("tools", "This project on this computer",
        [
            Info("project.tools", "This project's engine, editor or Godot executable", keywords: ["choose", "engine folder", "unity editor", "editor configuration"]),
        ]);

    public override string IconKey => "IconSettingsTools";

    public override string Description => "This computer's choices for the project, as its menu makes them.";

    public override IEnumerable<SettingsSearchResult> SearchEntries =>
    [
        .. base.SearchEntries,
        .. Project.ToolsSearchLabels.Select(label => Entry("project.tools", label, null, null, [label])),
    ];
}
