using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// A folder group's name, from <b>Rename…</b> in the group's menu (DESIGN.md §4, "Grouped by folder"): a name of the
/// user's own in place of the folder's, for folders with the same name in different places. The shell remembers it for
/// the folder.
/// </summary>
public sealed partial class GroupNameEditorViewModel : ObservableObject
{
    private readonly Action<string?> _apply;

    public GroupNameEditorViewModel(TabGroupViewModel group, Action<string?> apply)
    {
        Group = group;
        _apply = apply;
        Name = group.Label;
    }

    public TabGroupViewModel Group { get; }

    /// <summary>The name as it's typed, starting from what the label shows.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>What the label shows when the name is empty: the folder's own.</summary>
    public string Placeholder => Group.FolderLabel;

    /// <summary>Raised when the name is saved or the editor cancelled: it closes.</summary>
    public event Action? Done;

    /// <summary>Saves the name. An empty one, or the folder's own label, goes back to the folder's name.</summary>
    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim();
        _apply(name.Length == 0 || name == Group.FolderLabel ? null : name);
        Done?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Done?.Invoke();
}
