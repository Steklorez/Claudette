using System.Collections.Concurrent;
using Claudette.Core.Diffs;
using Claudette.Core.Git;

namespace Claudette.App.ViewModels;

/// <summary>
/// The working trees the session's changes are in (DESIGN.md §8, "Working trees"): the tab's own, and the worktrees
/// Claude changed files in, the main checkout's included, each its own group of rows.
/// </summary>
public sealed partial class ChangedFilesViewModel
{
    /// <summary>
    /// The top of the working tree each folder Claude changed files in belongs to, or null outside one. Found while the
    /// folder is there and kept, so the files of a worktree removed since stay together.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _workTreeOfFolder = new(ChangedFiles.PlatformPathComparer);

    /// <summary>The working trees Claude changed files in, as far as the rows have looked: worktree cleanup leaves them be.</summary>
    internal IReadOnlyList<string> ChangedWorkTrees => [.. _workTreeOfFolder.Values.OfType<string>().Distinct(ChangedFiles.PlatformPathComparer)];

    /// <summary>The top of the working tree <paramref name="path"/> is in, or null outside one. Reads files, so off the UI thread.</summary>
    private string? WorkTreeOf(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } folder || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }
        if (_workTreeOfFolder.TryGetValue(folder, out var known))
        {
            return known;
        }
        var tree = FindWorkTree(folder);
        if (Directory.Exists(folder))
        {
            _workTreeOfFolder[folder] = tree;
        }
        return tree;
    }

    /// <summary>
    /// The top of the working tree <paramref name="folder"/> is in: a worktree Claude Code made
    /// (<c>&lt;checkout&gt;/.claude/worktrees/&lt;name&gt;</c>) whether it's there or not, else the one git finds from the
    /// nearest folder that's there, else, when that's in none, the first folder that's gone (a worktree removed since).
    /// </summary>
    internal static string? FindWorkTree(string folder)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            if (GitWorktrees.MainCheckoutOf(at) is not null)
            {
                return at;
            }
        }
        string? gone = null;
        var existing = folder;
        while (!Directory.Exists(existing))
        {
            gone = existing;
            existing = Path.GetDirectoryName(existing);
            if (existing is null)
            {
                return gone;
            }
        }
        return GitInfo.TryGetWorkTree(existing)?.WorkTree ?? gone;
    }

    /// <summary>A group's heading: the working tree's folder and branch, such as <i>"brisk-otter · worktree-brisk-otter"</i>, or that it's gone.</summary>
    private static string WorkTreeLabel(string tree)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(tree));
        return !Directory.Exists(tree) ? $"{name} · removed"
            : GitInfo.TryGetBranch(tree) is { } branch ? $"{name} · {branch}"
            : name;
    }

    /// <summary>
    /// <paramref name="rows"/> in groups, one per working tree: the tab's own first (<paramref name="home"/>, which also
    /// has the files in none), then the others in the order Claude first changed them. Each group's first row has its
    /// heading, unless the tab's own is the only one. Reads files, so off the UI thread.
    /// </summary>
    private static List<ChangedFileRow> InGroups(List<ChangedFileRow> rows, string? home, string folder)
    {
        var groups = rows
            .GroupBy(row => IsHome(row.WorkTree, home) ? "" : row.WorkTree!, ChangedFiles.PlatformPathComparer)
            .OrderBy(group => group.Key.Length == 0 ? 0 : 1)
            .ToList();
        if (groups is [] or [{ Key.Length: 0 }])
        {
            return rows;
        }
        var grouped = new List<ChangedFileRow>(rows.Count);
        foreach (var group in groups)
        {
            var heading = group.Key.Length == 0
                ? home is not null ? WorkTreeLabel(home) : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))
                : WorkTreeLabel(group.Key);
            foreach (var row in group)
            {
                row.Group = heading;
                heading = null;
                grouped.Add(row);
            }
        }
        return grouped;
    }

    /// <summary>Whether <paramref name="tree"/> is the tab's own working tree, or none.</summary>
    private static bool IsHome(string? tree, string? home) =>
        tree is null || home is not null && ChangedFiles.PlatformPathComparer.Equals(Path.TrimEndingDirectorySeparator(tree), Path.TrimEndingDirectorySeparator(home));
}
