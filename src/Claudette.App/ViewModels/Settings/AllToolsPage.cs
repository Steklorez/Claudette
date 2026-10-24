using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// Settings → Project tools → All tools (DESIGN.md §18, "Project tools"): what every kind of project shares, how
/// solutions open and the editors' logs. Each kind of project has a page of its own (<see cref="ProjectTypePage"/>).
/// Kept on each machine, like the diff tool.
/// </summary>
public sealed partial class AllToolsPage : SettingsPage
{
    public AllToolsPage(SettingsContext context) : base(context, SettingsCategory.AllTools)
    {
        Section("solutions", "Solutions",
        [
            Setting("projectTools.openSolutionsWith", "Open solutions with", s => s.ProjectTools.OpenSolutionsWith, (s, v) => s.ProjectTools.OpenSolutionsWith = v,
                keywords: ["ide", "rider", "visual studio", "vs code"]),
            Setting("projectTools.customIdePath", "Program", s => s.ProjectTools.CustomIdePath, (s, v) => s.ProjectTools.CustomIdePath = v,
                "It's given the solution's path.", keywords: ["ide", "another program"]),
        ]);
        Section("editorLogs", "Editor logs",
        [
            Setting("projectTools.showEditorLogs", "Show editor logs under the tab", s => s.ProjectTools.ShowEditorLogs, (s, v) => s.ProjectTools.ShowEditorLogs = v,
                "While the editor, a commandlet or a batch run is going, its log is a row under one of the project's tabs.",
                "Read as it's written, without holding the file open. The row stays after the editor closes.", ["log", "unreal", "unity", "godot"]),
        ]);
    }

    public override string IconKey => "IconSettingsAllTools";

    public override string Description => "What every kind of project shares: how solutions open, and the editors' logs.";

    /// <summary>Found by the group's name too, as the one page Project tools was.</summary>
    protected override SettingsSearchResult Entry(string key, string label, string? description, string? section, IEnumerable<string> words) =>
        base.Entry(key, label, description, section, [.. words, SettingsSidebar.ProjectTools]);

    // ---- Solutions ---------------------------------------------------------------------------------------------------

    public IReadOnlyList<SettingChoice<SolutionOpener>> SolutionOpenerChoices { get; } =
    [
        new(SolutionOpener.System, "The app the OS uses for it"),
        new(SolutionOpener.Rider, "Rider"),
        new(SolutionOpener.VisualStudio, "Visual Studio"),
        new(SolutionOpener.VSCode, "VS Code"),
        new(SolutionOpener.Custom, "Another program…"),
    ];

    public SettingChoice<SolutionOpener> OpenSolutionsWith
    {
        get => SolutionOpenerChoices.First(c => c.Value == Settings.ProjectTools.OpenSolutionsWith);
        set
        {
            if (value is not null)
            {
                Set(value.Value, v => Settings.ProjectTools.OpenSolutionsWith = v);
                OnPropertyChanged(nameof(IsCustomIde));
            }
        }
    }

    public bool IsCustomIde => Settings.ProjectTools.OpenSolutionsWith == SolutionOpener.Custom;

    /// <summary>The program <b>Open solution</b> starts with the solution's path, for "Another program…".</summary>
    public string CustomIdePath
    {
        get => Settings.ProjectTools.CustomIdePath ?? "";
        set => Set(value, v => Settings.ProjectTools.CustomIdePath = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    [RelayCommand]
    private async Task BrowseCustomIdeAsync()
    {
        if (await Services.Platform.PickFileAsync("Choose the program to open solutions with") is { } path)
        {
            CustomIdePath = path;
        }
    }

    // ---- Editor logs (DESIGN.md §18, "Editor logs") -------------------------------------------------------------------

    public bool ShowEditorLogs
    {
        get => Settings.ProjectTools.ShowEditorLogs;
        set => Set(value, v => Settings.ProjectTools.ShowEditorLogs = v);
    }
}
