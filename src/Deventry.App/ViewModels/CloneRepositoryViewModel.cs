using Deventry.App.Services;
using Deventry.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// <b>New project…</b> (DESIGN.md §4, "Opening a tab"): an empty project, a new folder that can start as a git
/// repository, or <b>Clone a repository…</b>, a git URL cloned into a folder; either opens as a new tab, as
/// <see cref="ShellViewModel.OpenFolderAsync"/> opens any folder.
/// </summary>
public sealed partial class CloneRepositoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly GitClone _clone;
    private bool _nameEdited;
    private bool _settingName;

    public CloneRepositoryViewModel(AppServices services, ShellViewModel shell, bool empty = false)
    {
        _services = services;
        _shell = shell;
        _clone = new GitClone(services.Git);
        Parent = DefaultParent();
        IsEmpty = empty;
    }

    /// <summary>An empty project, rather than a clone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloneMode), nameof(Problem), nameof(HasProblem), nameof(CanClone), nameof(CloneText), nameof(Title), nameof(IntoText), nameof(TargetText))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial bool IsEmpty { get; set; }

    public bool IsCloneMode
    {
        get => !IsEmpty;
        set => IsEmpty = !value;
    }

    /// <summary>With an empty project: start a git repository in it.</summary>
    [ObservableProperty]
    public partial bool InitGit { get; set; } = true;

    public string Title => IsEmpty ? "New project" : "Clone a repository";

    public string IntoText => IsEmpty ? "Create in" : "Clone into";

    public string TargetText => IsEmpty ? $"The project will be at {Target}" : $"The clone will be at {Target}";

    /// <summary>The repository's address: anything <c>git clone</c> takes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(CanClone))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Url { get; set; } = "";

    /// <summary>The folder the clone goes into.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(CanClone), nameof(Target), nameof(TargetText))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Parent { get; set; } = "";

    /// <summary>The clone's folder name: filled in from the address until the user changes it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(CanClone), nameof(Target), nameof(TargetText))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Name { get; set; } = "";

    /// <summary>What git said when the clone failed; cleared by the next try.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClone), nameof(CloneText))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand), nameof(BrowseCommand), nameof(CancelCommand))]
    public partial bool IsCloning { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Why the clone can't start yet, shown once something has been typed; null when it can.</summary>
    public string? Problem => IsEmpty
        ? (Name.Length == 0 ? null : GitClone.FolderProblem(Parent, Name))
        : (Url.Length == 0 && !_nameEdited ? null : GitClone.Problem(Url, Parent, Name));

    public bool HasProblem => Problem is not null;

    public bool CanClone => !IsCloning && (IsEmpty ? GitClone.FolderProblem(Parent, Name) : GitClone.Problem(Url, Parent, Name)) is null;

    public string CloneText => IsEmpty ? (IsCloning ? "Creating…" : "Create") : (IsCloning ? "Cloning…" : "Clone");

    /// <summary>Where the clone will be, for the user to check.</summary>
    public string Target => Parent.Length > 0 && Name.Trim().Length > 0 ? Path.Combine(Parent, Name.Trim()) : "";

    partial void OnUrlChanged(string value)
    {
        Error = null;
        if (!_nameEdited && !IsEmpty)
        {
            SetNameFromUrl(value);
        }
    }

    partial void OnNameChanged(string value)
    {
        Error = null;
        // Typed by the user, not filled in from the address: from now on it's theirs.
        if (!_settingName)
        {
            _nameEdited = value.Length > 0;
            if (!_nameEdited && !IsEmpty)
            {
                SetNameFromUrl(Url);
            }
        }
    }

    partial void OnParentChanged(string value) => Error = null;

    private void SetNameFromUrl(string url)
    {
        _settingName = true;
        try
        {
            Name = GitClone.DefaultName(url);
        }
        finally
        {
            _settingName = false;
        }
    }

    /// <summary>The parent of the folder a tab last opened in, or the home folder.</summary>
    private string DefaultParent()
    {
        foreach (var recent in _services.State.RecentFolders)
        {
            if (Path.GetDirectoryName(recent.Path) is { Length: > 0 } parent && Directory.Exists(parent))
            {
                return parent;
            }
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private bool NotCloning => !IsCloning;

    [RelayCommand(CanExecute = nameof(NotCloning))]
    private async Task BrowseAsync()
    {
        if (await _services.Platform.PickFolderAsync(IsEmpty ? "Choose the folder to create the project in" : "Choose the folder to clone into") is { } folder)
        {
            Parent = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClone))]
    private async Task CloneAsync()
    {
        if (!CanClone)
        {
            return;
        }
        IsCloning = true;
        Error = null;
        try
        {
            var result = IsEmpty ? await _clone.CreateEmptyAsync(Parent, Name, InitGit) : await _clone.CloneAsync(Url, Parent, Name);
            // Done before the dialog closes: CloseClone keeps it while a clone runs.
            IsCloning = false;
            if (result.Path is { } path)
            {
                _shell.CloseClone();
                await _shell.OpenFolderAsync(path);
            }
            else
            {
                Error = result.Error;
            }
        }
        finally
        {
            IsCloning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotCloning))]
    private void Cancel() => _shell.CloseClone();
}
