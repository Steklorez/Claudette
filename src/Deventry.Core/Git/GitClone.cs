using Deventry.Core.Development;

namespace Deventry.Core.Git;

/// <summary>What <see cref="GitClone.CloneAsync"/> made: the clone's folder, or why there isn't one.</summary>
/// <param name="Path">The folder the repository was cloned into; null when it failed.</param>
/// <param name="Error">What went wrong, written for the user, with what git said; null when it worked.</param>
public sealed record GitCloneResult(string? Path, string? Error)
{
    public bool Succeeded => Path is not null;
}

/// <summary>
/// <b>Clone a repository…</b> (DESIGN.md §4, "Opening a tab"): <c>git clone &lt;url&gt;</c> into <c>&lt;parent&gt;/&lt;name&gt;</c>, with
/// the git every other git Deventry runs uses (<see cref="GitWorkingTree"/>: Settings → Git → <b>Path to git</b>, the
/// user environment). It never asks for a password: a repository that needs one fails with git's message instead.
/// </summary>
public sealed class GitClone(GitWorkingTree git)
{
    /// <summary>How long a clone may take: a large repository over a slow line is minutes, not git's usual seconds.</summary>
    public static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The folder name git would give a clone of <paramref name="url"/>: its last path segment without <c>.git</c>, for
    /// <c>https://github.com/org/repo.git</c>, <c>git@github.com:org/repo.git</c> and a local path alike. Empty when the
    /// URL has none yet.
    /// </summary>
    public static string DefaultName(string url)
    {
        var trimmed = url.Trim().TrimEnd('/', '\\');
        if (trimmed.Length == 0)
        {
            return "";
        }
        var start = trimmed.LastIndexOfAny(['/', '\\', ':']) + 1;
        var name = trimmed[start..];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
        {
            name = name[..^4];
        }
        return name;
    }

    /// <summary>
    /// Why the clone can't start, written for the user, or null when it can: the URL is empty, the parent folder
    /// doesn't exist, the name isn't a single folder name, or <c>&lt;parent&gt;/&lt;name&gt;</c> is already there.
    /// </summary>
    public static string? Problem(string url, string parent, string name) =>
        string.IsNullOrWhiteSpace(url) ? "Enter the repository's address." : FolderProblem(parent, name, "clone into", "the clone's folder");

    /// <summary>
    /// Why a new folder can't be made at <c>&lt;parent&gt;/&lt;name&gt;</c>, or null when it can: the parent folder
    /// doesn't exist, the name isn't a single folder name, or something is already there.
    /// </summary>
    public static string? FolderProblem(string parent, string name, string intoWhat = "create the project in", string whose = "the project's folder")
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return $"Choose the folder to {intoWhat}.";
        }
        if (!Directory.Exists(parent))
        {
            return "That folder doesn't exist.";
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"Enter a name for {whose}.";
        }
        var trimmedName = name.Trim();
        if (trimmedName is "." or ".." || trimmedName.IndexOfAny(['/', '\\']) >= 0 || trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The name must be a single folder name, without slashes.";
        }
        if (Directory.Exists(Path.Combine(parent, trimmedName)) || File.Exists(Path.Combine(parent, trimmedName)))
        {
            return $"There's already a folder named {trimmedName} there.";
        }
        return null;
    }

    /// <summary>
    /// Clones <paramref name="url"/> into <c>&lt;parent&gt;/&lt;name&gt;</c>, run from the parent folder, after
    /// <see cref="Problem"/> finds nothing wrong. Returns the clone's folder, or git's error.
    /// </summary>
    public async Task<GitCloneResult> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken = default)
    {
        if (Problem(url, parent, name) is { } problem)
        {
            return new GitCloneResult(null, problem);
        }
        var target = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), name.Trim());
        var result = await git.RunAsync(parent, ["clone", "--", url.Trim(), target], CloneTimeout, cancellationToken, SourceCheckout.NoPrompts).ConfigureAwait(false);
        if (result is null)
        {
            return new GitCloneResult(null, "Git couldn't be run, or the clone didn't finish in time.");
        }
        if (result.ExitCode != 0)
        {
            return new GitCloneResult(null, ErrorText(result.StandardError) ?? $"git clone failed (exit code {result.ExitCode}).");
        }
        return Directory.Exists(target)
            ? new GitCloneResult(target, null)
            : new GitCloneResult(null, "Git finished, but the clone's folder isn't there.");
    }

    /// <summary>
    /// A new, empty project: makes <c>&lt;parent&gt;/&lt;name&gt;</c>, and with <paramref name="initGit"/> starts a git
    /// repository in it, after <see cref="FolderProblem"/> finds nothing wrong. Returns the folder, or why it couldn't;
    /// a folder made whose <c>git init</c> failed is still returned, with git's error as well.
    /// </summary>
    public async Task<GitCloneResult> CreateEmptyAsync(string parent, string name, bool initGit, CancellationToken cancellationToken = default)
    {
        if (FolderProblem(parent, name) is { } problem)
        {
            return new GitCloneResult(null, problem);
        }
        var target = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), name.Trim());
        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new GitCloneResult(null, $"Couldn't make the folder: {e.Message}");
        }
        if (!initGit)
        {
            return new GitCloneResult(target, null);
        }
        var result = await git.RunAsync(target, ["init"], TimeSpan.FromMinutes(1), cancellationToken, SourceCheckout.NoPrompts).ConfigureAwait(false);
        return result is { ExitCode: 0 }
            ? new GitCloneResult(target, null)
            : new GitCloneResult(target, result is null ? "Git couldn't be run, so the folder isn't a repository yet." : ErrorText(result.StandardError) ?? "git init failed.");
    }

    /// <summary>Git's error lines, without its progress and the <c>fatal:</c> prefix on the last one.</summary>
    private static string? ErrorText(string standardError)
    {
        var lines = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("Cloning into", StringComparison.Ordinal) && !l.Contains("%", StringComparison.Ordinal))
            .Select(l => l.StartsWith("fatal: ", StringComparison.Ordinal) ? l["fatal: ".Length..] : l)
            .ToList();
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }
}
