namespace Claudette.App.ViewModels;

/// <summary>
/// Changed files across the tabs (DESIGN.md §8, "This session's changes"): who changed a file since, and the other
/// tabs looking again when one's Claude changes a file.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary><paramref name="changer"/>'s Claude changed <paramref name="path"/>: every other tab that lists it, or shows its working tree, looks again.</summary>
    internal void OnClaudeChangedFile(TabViewModel changer, string path)
    {
        foreach (var tab in AllTabs.Where(t => !ReferenceEquals(t, changer)))
        {
            tab.ChangedFiles.OnOtherTabChanged(path);
        }
    }

    /// <summary>
    /// The tab whose Claude changed <paramref name="path"/> last, after <paramref name="since"/>, other than
    /// <paramref name="asking"/>: its name, with how many others did too, or null when none did, so it was you, a
    /// command or something outside Claudette.
    /// </summary>
    internal string? ChangedSinceBy(TabViewModel asking, string path, DateTimeOffset since)
    {
        var later = AllTabs
            .Where(tab => !ReferenceEquals(tab, asking))
            .Select(tab => (Tab: tab, At: tab.ChangedFiles.LastClaudeChange(path)))
            .Where(t => t.At > since)
            .OrderByDescending(t => t.At)
            .ToArray();
        return later switch
        {
            [] => null,
            [var only] => only.Tab.DisplayName,
            [var latest, ..] => $"{latest.Tab.DisplayName} and {(later.Length == 2 ? "1 other tab" : $"{later.Length - 1} other tabs")}",
        };
    }
}
