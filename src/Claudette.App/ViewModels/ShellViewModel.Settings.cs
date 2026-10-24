using Claudette.App.ViewModels.Settings;

namespace Claudette.App.ViewModels;

/// <summary>
/// What the Settings window follows of the main window (DESIGN.md §14): it isn't modal, so its project pages follow the
/// tab selected here, its settings say which open tabs have their own (Tab settings…), and it opens where it was left.
/// </summary>
public sealed partial class ShellViewModel : ISettingsHost
{
    private TabViewModel? _settingsProjectTab;

    public ProjectSettingsViewModel? SelectedTabProject() => ProjectTab is { } tab ? new ProjectSettingsViewModel(_services, tab, this) : null;

    public event Action? SelectedTabChanged;

    public IReadOnlyList<string> TabsWithOwn(string overrideKey) =>
        [.. AllTabs.Where(t => TabOverrideKeys.Has(t.State.Overrides, overrideKey)).Select(t => t.DisplayName)];

    /// <summary>The page Settings showed when it last closed, this run; it opens there next.</summary>
    public string? LastSettingsPage { get; set; }

    /// <summary>Settings' project pages follow the tab whose project the sidebar's foot shows, as it changes.</summary>
    private void TellSettingsOfProjectTab()
    {
        if (!ReferenceEquals(_settingsProjectTab, ProjectTab))
        {
            _settingsProjectTab = ProjectTab;
            SelectedTabChanged?.Invoke();
        }
    }
}
