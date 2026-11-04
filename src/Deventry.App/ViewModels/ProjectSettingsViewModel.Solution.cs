using Deventry.App.Services;
using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The project's settings → <b>Solution</b> (DESIGN.md §14, "The project's settings"; §18, "deventry.json"): the file that
/// defines the project, usually its solution, which its menu offers to open, in <c>deventry.json</c> for everyone who opens
/// the project or in <c>deventry.local.json</c> for the user alone, whose wins. Each change is saved at once, and only
/// <c>solution</c> is rewritten; a file that isn't valid JSON is left alone.
/// </summary>
public sealed partial class ProjectSettingsViewModel
{
    private readonly Dictionary<ProjectFileScope, string?> _solutions = [];
    private readonly Dictionary<ProjectFileScope, string?> _solutionFileErrors = [];

    /// <summary>What the file dialog offers: solutions and workspaces first, then any file.</summary>
    public static IReadOnlyList<FileTypeFilter> SolutionTypes { get; } =
    [
        new("Solutions and workspaces", [.. ProjectSolution.SolutionExtensions.Select(extension => "*" + extension)]),
        new("All files", ["*"]),
    ];

    /// <summary>Which file the page shows and saves to: the shared one, or the user's own.</summary>
    [ObservableProperty]
    public partial SettingChoice<ProjectFileScope> SelectedSolutionFile { get; set; }

    partial void OnSelectedSolutionFileChanged(SettingChoice<ProjectFileScope> value)
    {
        SolutionError = null;
        SolutionFilesChanged();
    }

    private ProjectFileScope SolutionScope => SelectedSolutionFile.Value;

    /// <summary>The chosen file exists but can't be read, so its solution can't be changed here.</summary>
    public string? SolutionFileError => _solutionFileErrors.GetValueOrDefault(SolutionScope);

    public bool HasSolutionFileError => SolutionFileError is not null;

    private bool CanEditSolutionFile => SolutionFileError is null;

    /// <summary>Why the last change couldn't be saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSolutionError))]
    public partial string? SolutionError { get; private set; }

    public bool HasSolutionError => SolutionError is not null;

    /// <summary>The chosen file's <c>solution</c> as it has it, such as <c>NightOwl.sln</c>.</summary>
    private string? SolutionPath => _solutions.GetValueOrDefault(SolutionScope);

    /// <summary>What the chosen file holds: its solution, or that it has none.</summary>
    public string SolutionText => SolutionPath ?? $"None in {ProjectFile.FileName(Folder, SolutionScope)}.";

    /// <summary>
    /// What the menu does with it, or why it can't: opened with the IDE Settings names for a solution, with the OS's app for
    /// another file; it isn't there; the user's file's shows in its place; the project type's own <b>Open solution</b> stays.
    /// </summary>
    public string? SolutionNote
    {
        get
        {
            if (SolutionPath is not { } path)
            {
                return null;
            }
            var solution = new ProjectSolution(path, SolutionScope);
            if (solution.Resolve(Folder).Problem is { } problem)
            {
                return $"It can't be opened: {path} {problem}.";
            }
            if (SolutionScope == ProjectFileScope.Shared && _solutions.GetValueOrDefault(ProjectFileScope.Local) is not null)
            {
                return $"{ProjectFile.FileName(Folder, ProjectFileScope.Local)}'s solution shows in its place, for you.";
            }
            var opens = solution.IsSolution
                ? "The project's menu opens it with the app Settings → Project tools → All tools → Open solutions with says"
                : "The project's menu opens it with the app your system uses for it";
            return ToolsProject?.Actions.Any(a => a.Id == "open-solution") == true
                ? $"{opens}, as Open {solution.FileName}, beside {ToolsProject.KindName}'s own Open solution."
                : ToolsProject is null
                    ? $"{opens}, as Open {solution.FileName}, its main action (Run the project's main action)."
                    : $"{opens}, as Open {solution.FileName}.";
        }
    }

    public bool HasSolutionNote => SolutionNote is not null;

    private bool CanRemoveSolution => CanEditSolutionFile && SolutionPath is not null;

    /// <summary><b>Choose…</b>: a file for the chosen project file, from the project's folder when it's in it.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSolutionFile))]
    private async Task ChooseSolutionAsync()
    {
        if (await _services.Platform.PickFileAsync("Choose the project's solution", SolutionTypes) is not { } picked)
        {
            return;
        }
        SaveSolution(() => ProjectFile.WriteSolution(Folder, SolutionScope, ProjectFile.SolutionPathFor(Folder, picked)));
    }

    /// <summary><b>Remove</b>: the chosen file's solution goes, and the menu offers the other file's, or none.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSolution))]
    private void RemoveSolution() => SaveSolution(() => ProjectFile.WriteSolution(Folder, SolutionScope, null));

    private void LoadSolutionFiles()
    {
        foreach (var scope in new[] { ProjectFileScope.Shared, ProjectFileScope.Local })
        {
            try
            {
                _solutions[scope] = ProjectFile.ReadSolution(Folder, scope);
                _solutionFileErrors[scope] = null;
            }
            catch (InvalidOperationException ex)
            {
                _solutions[scope] = null;
                _solutionFileErrors[scope] = ex.Message;
            }
        }
    }

    /// <summary>The file the page opens on: the user's own when it has a solution, since that's the one the menu opens.</summary>
    private int SolutionFileToShow() => _solutions.GetValueOrDefault(ProjectFileScope.Local) is not null ? 1 : 0;

    /// <summary>Writes one change to the chosen file; the folder's tabs read it again, so the menu follows.</summary>
    private void SaveSolution(Action write)
    {
        var scope = SolutionScope;
        try
        {
            write();
            SolutionError = null;
            OnFileSaved();
        }
        catch (Exception ex) when (IsSaveFailure(ex))
        {
            SolutionError = SaveFailure(scope, ex);
        }
        LoadSolutionFiles();
        SolutionFilesChanged();
    }

    private void SolutionFilesChanged()
    {
        OnPropertyChanged(nameof(SolutionFileError));
        OnPropertyChanged(nameof(HasSolutionFileError));
        OnPropertyChanged(nameof(SolutionText));
        OnPropertyChanged(nameof(SolutionNote));
        OnPropertyChanged(nameof(HasSolutionNote));
        ChooseSolutionCommand.NotifyCanExecuteChanged();
        RemoveSolutionCommand.NotifyCanExecuteChanged();
    }
}
