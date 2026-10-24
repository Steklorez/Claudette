using Claudette.App.ViewModels.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Where the Settings window opens (DESIGN.md §14): a category or one of the project pages, the project pages of the
/// tab that was selected, a setting to bring into view, and whether to start a new entry on that page straight away
/// (<b>Add an action…</b> on Actions, <b>Add a link…</b> on Links).
/// </summary>
/// <param name="Project">The selected tab's pages; null with no tab open, and the group is hidden.</param>
/// <param name="Key">The setting or section to scroll to and pick out, such as <c>usage.warnPercent</c>.</param>
public sealed record SettingsOpening(string? Category = null, ProjectSettingsViewModel? Project = null, bool StartNew = false, string? Key = null);

/// <summary>
/// The group below the categories in Settings' sidebar (DESIGN.md §14, "The project's pages"): the selected tab's
/// project, with its Links, Actions and Tools pages (<see cref="Settings.ProjectPage"/>). The window isn't modal, so the
/// group follows the selected tab while it's open.
/// </summary>
public sealed partial class SettingsViewModel
{
    public const string LinksPage = "Links";

    public const string ActionsPage = "Actions";

    public const string ToolsPage = "Tools";

    /// <summary>The project group's pages, in the sidebar's order.</summary>
    public static readonly IReadOnlyList<string> ProjectPages = [LinksPage, ActionsPage, ToolsPage];

    /// <summary>The selected tab's project pages; null with no tab open.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasProject))]
    public partial ProjectSettingsViewModel? Project { get; private set; }

    public bool HasProject => Project is not null;

    /// <summary>Uses <paramref name="project"/>'s pages in place of the ones shown, which go: another tab was selected.</summary>
    private void UseProject(ProjectSettingsViewModel? project)
    {
        if (ReferenceEquals(project, Project) && Pages.Count > 0)
        {
            return;
        }
        var old = Project;
        Project = project;
        Pages =
        [
            .. _categoryPages,
            .. project is not null ? [new ProjectLinksPage(_context, project), new ProjectActionsPage(_context, project), new ProjectToolChoicesPage(_context, project)] : Array.Empty<SettingsPage>(),
        ];
        old?.Dispose();
        foreach (var page in Pages.OfType<ProjectPage>())
        {
            page.RefreshItems();
        }
        BuildNav();
        // A project page showing becomes the same page of the new tab's project, or General without one.
        if (ProjectPages.Contains(SelectedCategory) && project is null)
        {
            SelectedCategory = AllCategories[0];
        }
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(SelectedNavEntry));
    }

    /// <summary>Another tab was selected in the main window: the project pages are its project's.</summary>
    private void OnSelectedTabChanged() => UseProject(_host?.SelectedTabProject());

    /// <summary>From the Tools page: the app-wide defaults are in Settings → Project tools.</summary>
    [RelayCommand]
    private void ShowProjectToolsDefaults() => SelectedCategory = SettingsCategory.ProjectTools;
}
