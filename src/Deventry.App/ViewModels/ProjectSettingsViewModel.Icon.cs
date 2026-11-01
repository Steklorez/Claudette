using Deventry.App.Services;
using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// Settings → the tab's project → <b>Icon</b> (DESIGN.md §4, "Grouped by folder"; §14): the picture the folder's group
/// shows in the sidebar in place of its colored dot, in <c>deventry.json</c> for everyone who opens the project or in
/// <c>deventry.local.json</c> for the user alone, whose wins. Choosing one saves it at once, as the other pages do, and
/// only <c>icon</c> is rewritten; a file that isn't valid JSON is left alone.
/// </summary>
public sealed partial class ProjectSettingsViewModel
{
    private readonly Dictionary<ProjectFileScope, string?> _icons = [];
    private readonly Dictionary<ProjectFileScope, string?> _iconFileErrors = [];

    /// <summary>The pictures an icon can be, for the file dialog.</summary>
    public static IReadOnlyList<FileTypeFilter> IconTypes { get; } =
        [new("Pictures (PNG, ICO, JPEG, BMP, GIF, WebP)", [.. ProjectFile.IconExtensions.Select(extension => "*" + extension)])];

    /// <summary>Which file the page shows and saves to: the shared one, or the user's own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconText), nameof(IconPreview), nameof(HasIconPreview), nameof(IconNote), nameof(HasIconNote), nameof(IconFileError), nameof(HasIconFileError))]
    [NotifyCanExecuteChangedFor(nameof(ChooseIconCommand), nameof(RemoveIconCommand))]
    public partial SettingChoice<ProjectFileScope> SelectedIconFile { get; set; }

    partial void OnSelectedIconFileChanged(SettingChoice<ProjectFileScope> value) => IconError = null;

    private ProjectFileScope IconScope => SelectedIconFile.Value;

    /// <summary>The chosen file's icon as it has it, such as <c>Assets/logo.png</c>.</summary>
    private string? IconPath => _icons.GetValueOrDefault(IconScope);

    private bool HasIconPath => IconPath is not null;

    /// <summary>What the chosen file holds: its icon's path, or that it has none.</summary>
    public string IconText => IconPath ?? $"None in {ProjectFile.FileName(Folder, IconScope)}: the group shows its colored dot.";

    /// <summary>The chosen file's picture, to show beside its path, when it can be shown.</summary>
    public string? IconPreview => IconPath is { } path ? ProjectFile.ResolveIcon(Folder, path).FullPath : null;

    public bool HasIconPreview => IconPreview is not null;

    /// <summary>
    /// Why the chosen file's icon doesn't show; that the other file's shows in its place; or that a picture outside the
    /// project is saved by a path only this computer has.
    /// </summary>
    public string? IconNote
    {
        get
        {
            if (IconPath is not { } path)
            {
                return IconScope == ProjectFileScope.Shared && _icons.GetValueOrDefault(ProjectFileScope.Local) is not null
                    ? $"{ProjectFile.FileName(Folder, ProjectFileScope.Local)}'s icon shows, for you." : null;
            }
            if (ProjectFile.ResolveIcon(Folder, path).Problem is { } problem)
            {
                return $"It doesn't show: {char.ToUpperInvariant(problem[0])}{problem[1..]}";
            }
            if (IconScope == ProjectFileScope.Shared && _icons.GetValueOrDefault(ProjectFileScope.Local) is not null)
            {
                return $"{ProjectFile.FileName(Folder, ProjectFileScope.Local)}'s icon shows in its place, for you.";
            }
            return Path.IsPathRooted(path) && IconScope == ProjectFileScope.Shared
                ? "It's outside the project's folder, so it's saved by its full path, which only this computer has."
                : null;
        }
    }

    public bool HasIconNote => IconNote is not null;

    /// <summary>The chosen file exists but can't be read, so its icon can't be changed here.</summary>
    public string? IconFileError => _iconFileErrors.GetValueOrDefault(IconScope);

    public bool HasIconFileError => IconFileError is not null;

    private bool CanEditIconFile => IconFileError is null;

    private bool CanRemoveIcon => CanEditIconFile && HasIconPath;

    /// <summary>Why the last choice couldn't be saved, or why the picture can't be the icon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIconError))]
    public partial string? IconError { get; private set; }

    public bool HasIconError => IconError is not null;

    private void LoadIcons()
    {
        foreach (var scope in new[] { ProjectFileScope.Shared, ProjectFileScope.Local })
        {
            try
            {
                _icons[scope] = ProjectFile.ReadIcon(Folder, scope);
                _iconFileErrors[scope] = null;
            }
            catch (InvalidOperationException ex)
            {
                _icons[scope] = null;
                _iconFileErrors[scope] = ex.Message;
            }
        }
    }

    /// <summary>The file the page opens on: the user's own when it has an icon, since that's the one that shows.</summary>
    private int IconFileToShow() => _icons.GetValueOrDefault(ProjectFileScope.Local) is not null ? 1 : 0;

    /// <summary><b>Choose…</b>: a picture for the chosen file, from the project's folder when it's in it.</summary>
    [RelayCommand(CanExecute = nameof(CanEditIconFile))]
    private async Task ChooseIconAsync()
    {
        if (await _services.Platform.PickFileAsync("Choose the group's icon", IconTypes) is not { } picked)
        {
            return;
        }
        if (ProjectFile.ResolveIcon(Folder, picked).Problem is { } problem)
        {
            IconError = $"{char.ToUpperInvariant(problem[0])}{problem[1..]}";
            return;
        }
        SaveIcon(ProjectFile.IconPathFor(Folder, picked));
    }

    /// <summary><b>Remove</b>: the chosen file's icon goes, and the group shows the other file's, or its dot.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveIcon))]
    private void RemoveIcon() => SaveIcon(null);

    private void SaveIcon(string? icon)
    {
        var scope = IconScope;
        try
        {
            ProjectFile.WriteIcon(Folder, scope, icon);
            IconError = null;
            OnFileSaved();
        }
        catch (Exception ex) when (IsSaveFailure(ex))
        {
            IconError = SaveFailure(scope, ex);
        }
        LoadIcons();
        IconsChanged();
    }

    private void IconsChanged()
    {
        OnPropertyChanged(nameof(IconText));
        OnPropertyChanged(nameof(IconPreview));
        OnPropertyChanged(nameof(HasIconPreview));
        OnPropertyChanged(nameof(IconNote));
        OnPropertyChanged(nameof(HasIconNote));
        OnPropertyChanged(nameof(IconFileError));
        OnPropertyChanged(nameof(HasIconFileError));
        ChooseIconCommand.NotifyCanExecuteChanged();
        RemoveIconCommand.NotifyCanExecuteChanged();
    }
}
