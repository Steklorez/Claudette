using Claudette.Core.Settings;

namespace Claudette.App.ViewModels;

/// <summary>
/// The names the user gives tab groups (DESIGN.md §4, "Grouped by folder"), remembered for each folder in the app state
/// and matched as groups match folders. Nothing here saves the state.
/// </summary>
internal static class GroupNames
{
    /// <summary>The name saved for a folder's group, or null for none.</summary>
    public static string? Saved(AppState state, string folder) =>
        state.FolderNames.FirstOrDefault(p => FolderHistory.SamePath(p.Key, folder)).Value;

    /// <summary>Saves <paramref name="name"/> for <paramref name="folder"/>, in place of any it had under another spelling; null forgets it.</summary>
    public static void Remember(AppState state, string folder, string? name)
    {
        foreach (var path in state.FolderNames.Keys.Where(p => FolderHistory.SamePath(p, folder)).ToList())
        {
            state.FolderNames.Remove(path);
        }
        if (name is not null)
        {
            state.FolderNames[folder] = name;
        }
    }
}
