using Avalonia.Media;
using Deventry.App.Services;
using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The project's settings → <b>Group</b> (DESIGN.md §4, "Grouped by folder"; §14): how the folder's group looks in
/// the sidebar, its color and the picture in place of its dot, in <c>deventry.json</c> for everyone who opens the
/// project or in <c>deventry.local.json</c> for the user alone, whose wins. Each change is saved at once, as on the other
/// pages, and only <c>color</c> or <c>icon</c> is rewritten; a file that isn't valid JSON is left alone.
/// </summary>
public sealed partial class ProjectSettingsViewModel
{
    private readonly Dictionary<ProjectFileScope, string?> _icons = [];
    private readonly Dictionary<ProjectFileScope, string?> _colors = [];
    private readonly Dictionary<ProjectFileScope, string?> _groupFileErrors = [];

    /// <summary>The pictures an icon can be, for the file dialog.</summary>
    public static IReadOnlyList<FileTypeFilter> IconTypes { get; } =
        [new("Pictures (PNG, ICO, JPEG, BMP, GIF, WebP)", [.. ProjectFile.IconExtensions.Select(extension => "*" + extension)])];

    /// <summary>Which file the page shows and saves to: the shared one, or the user's own.</summary>
    [ObservableProperty]
    public partial SettingChoice<ProjectFileScope> SelectedGroupFile { get; set; }

    partial void OnSelectedGroupFileChanged(SettingChoice<ProjectFileScope> value)
    {
        GroupError = null;
        GroupFilesChanged();
    }

    private ProjectFileScope GroupScope => SelectedGroupFile.Value;

    private string GroupFileName(ProjectFileScope scope) => ProjectFile.FileName(Folder, scope);

    /// <summary>The chosen file exists but can't be read, so the group's look can't be changed in it here.</summary>
    public string? GroupFileError => _groupFileErrors.GetValueOrDefault(GroupScope);

    public bool HasGroupFileError => GroupFileError is not null;

    private bool CanEditGroupFile => GroupFileError is null;

    /// <summary>Why the last change couldn't be saved, or why the picture can't be the icon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroupError))]
    public partial string? GroupError { get; private set; }

    public bool HasGroupError => GroupError is not null;

    /// <summary>"…'s color shows in its place, for you.": the user's file wins over the shared one.</summary>
    private string? ShownInstead(Dictionary<ProjectFileScope, string?> values, string what) =>
        GroupScope == ProjectFileScope.Shared && values.GetValueOrDefault(ProjectFileScope.Local) is not null
            ? $"{GroupFileName(ProjectFileScope.Local)}'s {what} shows in its place, for you." : null;

    // ---- Color -------------------------------------------------------------------------------------------------------

    private string? ColorHex => _colors.GetValueOrDefault(GroupScope);

    /// <summary>What the chosen file holds: its color, or that it has none.</summary>
    public string ColorText => ColorHex ?? $"None in {GroupFileName(GroupScope)}: the group has the color remembered on this computer.";

    /// <summary>The chosen file's color, to show beside it.</summary>
    public IBrush? ColorBrush => ColorHex is { } hex && TabGroupViewModel.TryParseHex(hex, out var color) ? new SolidColorBrush(color) : null;

    public bool HasColorBrush => ColorBrush is not null;

    public string? ColorNote => ColorHex is null ? null : ShownInstead(_colors, "color");

    public bool HasColorNote => ColorNote is not null;

    /// <summary><b>Choose…</b> beside the color: the folder has a group in the sidebar to color, and the chosen file can take it.</summary>
    public bool CanChooseColor => CanEditGroupFile && Group is not null;

    private bool CanRemoveColor => CanEditGroupFile && ColorHex is not null;

    /// <summary>
    /// <b>Choose…</b>: the group's color picker, opening on the color the group has now. What it picks shows on the group
    /// at once and is remembered on this computer; the color it closes on is saved in the chosen file. Null when there's
    /// no group to color.
    /// </summary>
    public GroupColorPickerViewModel? PickColor() => Group is { } group && CanEditGroupFile
        ? new(group, color => _shell.SetGroupColor(group, color), color => SaveGroup(() => ProjectFile.WriteColor(Folder, GroupScope, TabGroupViewModel.ToHex(color))))
        : null;

    /// <summary><b>Remove</b>: the chosen file's color goes; the group keeps the color it has, remembered on this computer.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveColor))]
    private void RemoveColor() => SaveGroup(() => ProjectFile.WriteColor(Folder, GroupScope, null));

    // ---- Icon --------------------------------------------------------------------------------------------------------

    /// <summary>The chosen file's icon as it has it, such as <c>Assets/logo.png</c>.</summary>
    private string? IconPath => _icons.GetValueOrDefault(GroupScope);

    /// <summary>What the chosen file holds: its icon's path, or that it has none.</summary>
    public string IconText => IconPath ?? $"None in {GroupFileName(GroupScope)}: the group shows its colored dot.";

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
                return null;
            }
            if (ProjectFile.ResolveIcon(Folder, path).Problem is { } problem)
            {
                return $"It doesn't show: {char.ToUpperInvariant(problem[0])}{problem[1..]}";
            }
            return ShownInstead(_icons, "icon") ?? (Path.IsPathRooted(path) && GroupScope == ProjectFileScope.Shared
                ? "It's outside the project's folder, so it's saved by its full path, which only this computer has."
                : null);
        }
    }

    public bool HasIconNote => IconNote is not null;

    private bool CanRemoveIcon => CanEditGroupFile && IconPath is not null;

    /// <summary><b>Choose…</b>: a picture for the chosen file, from the project's folder when it's in it.</summary>
    [RelayCommand(CanExecute = nameof(CanEditGroupFile))]
    private async Task ChooseIconAsync()
    {
        if (await _services.Platform.PickFileAsync("Choose the group's icon", IconTypes) is not { } picked)
        {
            return;
        }
        if (ProjectFile.ResolveIcon(Folder, picked).Problem is { } problem)
        {
            GroupError = $"{char.ToUpperInvariant(problem[0])}{problem[1..]}";
            return;
        }
        SaveGroup(() => ProjectFile.WriteIcon(Folder, GroupScope, ProjectFile.IconPathFor(Folder, picked)));
    }

    /// <summary><b>Remove</b>: the chosen file's icon goes, and the group shows the other file's, or its dot.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveIcon))]
    private void RemoveIcon() => SaveGroup(() => ProjectFile.WriteIcon(Folder, GroupScope, null));

    // ---- Both --------------------------------------------------------------------------------------------------------

    private void LoadGroupFiles()
    {
        foreach (var scope in new[] { ProjectFileScope.Shared, ProjectFileScope.Local })
        {
            try
            {
                _icons[scope] = ProjectFile.ReadIcon(Folder, scope);
                _colors[scope] = ProjectFile.ReadColor(Folder, scope);
                _groupFileErrors[scope] = null;
            }
            catch (InvalidOperationException ex)
            {
                _icons[scope] = null;
                _colors[scope] = null;
                _groupFileErrors[scope] = ex.Message;
            }
        }
    }

    /// <summary>The file the page opens on: the user's own when it has a color or an icon, since that's the one that shows.</summary>
    private int GroupFileToShow() =>
        _icons.GetValueOrDefault(ProjectFileScope.Local) is not null || _colors.GetValueOrDefault(ProjectFileScope.Local) is not null ? 1 : 0;

    /// <summary>Writes one change to the chosen file; the folder's tabs read it again, so the group follows.</summary>
    private void SaveGroup(Action write)
    {
        var scope = GroupScope;
        try
        {
            write();
            GroupError = null;
            OnFileSaved();
        }
        catch (Exception ex) when (IsSaveFailure(ex))
        {
            GroupError = SaveFailure(scope, ex);
        }
        LoadGroupFiles();
        GroupFilesChanged();
    }

    private void GroupFilesChanged()
    {
        foreach (var name in new[]
        {
            nameof(GroupFileError), nameof(HasGroupFileError), nameof(ColorText), nameof(ColorBrush), nameof(HasColorBrush), nameof(ColorNote),
            nameof(HasColorNote), nameof(IconText), nameof(IconPreview), nameof(HasIconPreview), nameof(IconNote), nameof(HasIconNote),
        })
        {
            OnPropertyChanged(name);
        }
        OnPropertyChanged(nameof(CanChooseColor));
        RemoveColorCommand.NotifyCanExecuteChanged();
        ChooseIconCommand.NotifyCanExecuteChanged();
        RemoveIconCommand.NotifyCanExecuteChanged();
    }
}
