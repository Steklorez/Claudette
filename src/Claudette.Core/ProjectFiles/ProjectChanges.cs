using Claudette.Core.Diffs;

namespace Claudette.Core.ProjectFiles;

/// <summary>One open tab's changed files, as the Project Files tab gathers them.</summary>
public sealed record TabChanges(string TabId, string TabName, IReadOnlyList<ChangedFile> Files);

/// <summary>A tab that changed a file, and its latest change to it.</summary>
public sealed record ChangeAuthor(string TabId, string TabName, DateTimeOffset LastChanged, string? LatestChange);

/// <summary>
/// A file Claude changed in one or more of the project's open tabs, with what it held before the earliest of their
/// changes to it.
/// </summary>
/// <param name="Before">The file before Claude's first change in any of them; null for a new file, or when not known.</param>
/// <param name="Authors">The tabs that changed it, latest first.</param>
public sealed record ProjectFileChange(string Path, string? Before, bool BeforeKnown, bool IsNew, IReadOnlyList<ChangeAuthor> Authors)
{
    /// <summary>Who changed it, for the peek's header and the file's tip: <c>refactor auth</c>, or <c>refactor auth and fix login</c>.</summary>
    public string AuthorNames => Authors.Count switch
    {
        0 => "",
        1 => Authors[0].TabName,
        2 => $"{Authors[0].TabName} and {Authors[1].TabName}",
        _ => $"{string.Join(", ", Authors.Take(Authors.Count - 1).Select(a => a.TabName))} and {Authors[^1].TabName}",
    };

    public DateTimeOffset LastChanged => Authors.Count == 0 ? default : Authors.Max(a => a.LastChanged);
}

/// <summary>
/// The changes the Project Files tab marks (DESIGN.md §18, "Project files", "Claude's changes"): every file the project's
/// open tabs changed, with the "before" of the earliest change to it among them, so one tab's changes show as its
/// Changed files shows them, and several tabs' all show against what the file held before any of them.
/// </summary>
public static class ProjectChanges
{
    /// <summary>The files <paramref name="tabs"/> changed, by full path, matched as <paramref name="comparer"/> says.</summary>
    public static IReadOnlyDictionary<string, ProjectFileChange> Merge(IEnumerable<TabChanges> tabs, StringComparer? comparer = null)
    {
        comparer ??= ChangedFiles.PlatformPathComparer;
        var earliest = new Dictionary<string, ChangedFile>(comparer);
        var authors = new Dictionary<string, List<ChangeAuthor>>(comparer);
        foreach (var tab in tabs)
        {
            foreach (var file in tab.Files)
            {
                var path = Normalize(file.Path);
                if (!earliest.TryGetValue(path, out var first) || file.FirstChanged < first.FirstChanged)
                {
                    earliest[path] = file;
                }
                if (!authors.TryGetValue(path, out var list))
                {
                    authors[path] = list = [];
                }
                list.Add(new ChangeAuthor(tab.TabId, tab.TabName, file.LastChanged, ReviewedFiles.LatestChange(file)));
            }
        }
        var merged = new Dictionary<string, ProjectFileChange>(comparer);
        foreach (var (path, file) in earliest)
        {
            merged[path] = new ProjectFileChange(path, file.Before, file.BeforeKnown, file.IsNew,
                [.. authors[path].OrderByDescending(a => a.LastChanged)]);
        }
        return merged;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
