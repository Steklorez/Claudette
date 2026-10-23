using System.ComponentModel;
using System.Globalization;
using Claudette.Core.Git;
using Claudette.Core.Processes;

namespace Claudette.Core.Development;

/// <summary>A commit on the upstream that the checkout doesn't have yet.</summary>
public sealed record UpstreamCommit(string Hash, string Subject, string Author, DateTimeOffset When)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
}

/// <summary>Where a source build's checkout stands against the branch it tracks (DESIGN.md §9, "New commits upstream").</summary>
/// <param name="Branch">The branch checked out, such as <c>main</c>.</param>
/// <param name="Upstream">The branch it tracks, such as <c>origin/main</c>.</param>
/// <param name="UpstreamHead">The upstream's newest commit, in full.</param>
/// <param name="Behind">Commits on the upstream that the checkout doesn't have.</param>
/// <param name="Ahead">Commits in the checkout that the upstream doesn't have.</param>
/// <param name="HasChanges">Uncommitted changes to tracked files.</param>
/// <param name="Commits">The newest of the commits it's behind, at most <see cref="SourceCheckout.MaxCommits"/>, newest first.</param>
public sealed record UpstreamStatus(string Branch, string Upstream, string UpstreamHead, int Behind, int Ahead, bool HasChanges, IReadOnlyList<UpstreamCommit> Commits)
{
    /// <summary>Pulling is a fast-forward: there's something to pull, and nothing of the checkout's own in the way.</summary>
    public bool CanPull => Behind > 0 && Ahead == 0 && !HasChanges;

    /// <summary>Why the commits it's behind can't be pulled, or null when they can, or there are none.</summary>
    public string? WhyNotPull => Behind == 0 ? null
        : HasChanges ? "The checkout has uncommitted changes. Commit or stash them to pull."
        : Ahead > 0 ? $"The checkout has {SourceCheckout.Commits(Ahead)} that {Upstream} doesn't. Pull it yourself."
        : null;
}

/// <summary>What a check of the checkout found: where it stands, or why it couldn't be checked.</summary>
public sealed record SourceCheck(UpstreamStatus? Status, string? Problem)
{
    public static SourceCheck Failed(string problem) => new(null, problem);
}

/// <summary>How a build of the app went, with its last lines of output.</summary>
public sealed record SourceBuildResult(bool Succeeded, IReadOnlyList<string> LastLines, string? Error = null);

/// <summary>
/// A source build's checkout (DESIGN.md §9, "New commits upstream"): how it stands against the branch it tracks, pulling
/// that branch when it's a fast-forward, and building the app. Never merges, rebases, stashes or discards anything: the
/// checkout is often where Claudette is being worked on. Git never asks for a password, since there's no one to answer.
/// </summary>
public sealed class SourceCheckout(GitWorkingTree git, IProcessLauncher launcher, TimeProvider time, UserEnvironment? environment = null)
{
    /// <summary>The most commits a check lists.</summary>
    public const int MaxCommits = 20;

    /// <summary>The most lines of a build's output that are kept, for the dialog.</summary>
    public const int BuildLines = 12;

    /// <summary>How long a fetch or a fast-forward may take.</summary>
    public static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long a build may take.</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Keeps git and Git Credential Manager from asking for a password.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoPrompts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
    };

    private const char Separator = '\x1f';

    /// <summary>Fetches the checkout's upstream and says how the checkout stands against it.</summary>
    public async Task<SourceCheck> CheckAsync(string root, CancellationToken cancellationToken = default)
    {
        var branch = await git.RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return SourceCheck.Failed("Git couldn't read the checkout.");
        }
        if (branch.ExitCode != 0)
        {
            return SourceCheck.Failed("The checkout isn't on a branch, so there's no upstream to check.");
        }
        var name = branch.StandardOutput.Trim();
        var upstream = await git.RunAsync(root, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cancellationToken).ConfigureAwait(false);
        if (upstream is not { ExitCode: 0 } || upstream.StandardOutput.Trim() is not { Length: > 0 } upstreamName)
        {
            return SourceCheck.Failed($"The checkout's branch {name} doesn't track a remote branch, so there's nothing to check.");
        }
        var fetched = await git.RunAsync(root, ["fetch", "--quiet"], GitTimeout, cancellationToken, NoPrompts).ConfigureAwait(false);
        if (fetched is not { ExitCode: 0 })
        {
            return SourceCheck.Failed($"Couldn't fetch {upstreamName}: {GitError(fetched, "git fetch")}");
        }
        return await ReadAsync(root, name, upstreamName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches, and fast-forwards the checkout to its upstream when nothing of its own is in the way. Null once it's
    /// done (or there was nothing to pull), else why not.
    /// </summary>
    public async Task<string?> PullAsync(string root, CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(root, cancellationToken).ConfigureAwait(false);
        if (check.Status is not { } status)
        {
            return check.Problem;
        }
        if (status.WhyNotPull is { } why)
        {
            return why;
        }
        if (status.Behind == 0)
        {
            return null;
        }
        var merged = await git.RunAsync(root, ["merge", "--ff-only", "--quiet", "@{upstream}"], GitTimeout, cancellationToken, NoPrompts).ConfigureAwait(false);
        return merged is { ExitCode: 0 } ? null : $"Couldn't pull {status.Upstream}: {GitError(merged, "git merge")}";
    }

    /// <summary>
    /// Builds the app's project in the configuration <paramref name="build"/> was built in, from the checkout's top so
    /// <c>global.json</c> picks the SDK. Build servers are left out: they'd outlive the build and hold its output open.
    /// </summary>
    /// <param name="onLine">Each line of the build's output as it comes, on a background thread.</param>
    public async Task<SourceBuildResult> BuildAsync(SourceBuild build, Action<string>? onLine = null, CancellationToken cancellationToken = default)
    {
        if (build.ProjectDirectory is not { } project || build.Configuration is not { } configuration)
        {
            return new SourceBuildResult(false, [], "Claudette can't tell which project and configuration it was built from.");
        }
        var lines = new Queue<string>();
        void Collect(string line)
        {
            lock (lines)
            {
                lines.Enqueue(line);
                while (lines.Count > BuildLines)
                {
                    lines.Dequeue();
                }
            }
            onLine?.Invoke(line);
        }
        IReadOnlyList<string> Last()
        {
            lock (lines)
            {
                return [.. lines];
            }
        }

        try
        {
            var variables = environment is null ? null : await environment.GetAsync(cancellationToken).ConfigureAwait(false);
            var spec = new ProcessStartSpec(Dotnet(variables ?? UserEnvironment.Snapshot()),
                ["build", project, "-c", configuration, "--nologo", "-tl:off", "--disable-build-servers"]) { WorkingDirectory = build.RepositoryRoot };
            if (environment is not null)
            {
                spec = await environment.ApplyAsync(spec, cancellationToken).ConfigureAwait(false);
            }
            var result = await ProcessRunner.RunAsync(launcher, spec, BuildTimeout, time, cancellationToken, Collect).ConfigureAwait(false);
            return result.ExitCode == 0
                ? new SourceBuildResult(true, Last())
                : new SourceBuildResult(false, Last(), string.Create(CultureInfo.InvariantCulture, $"The build failed (exit code {result.ExitCode})."));
        }
        catch (TimeoutException)
        {
            return new SourceBuildResult(false, Last(), $"The build didn't finish within {BuildTimeout.TotalMinutes:0} minutes, so it was stopped.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SourceBuildResult(false, Last(), $"dotnet couldn't be started: {ex.Message}");
        }
    }

    /// <summary>The <c>dotnet</c> in <c>DOTNET_ROOT</c> when the environment sets one, else the one on its <c>PATH</c>.</summary>
    public static string Dotnet(IReadOnlyDictionary<string, string> environment)
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return environment.TryGetValue("DOTNET_ROOT", out var root) && root.Length > 0 && File.Exists(Path.Combine(root, name))
            ? Path.Combine(root, name)
            : "dotnet";
    }

    /// <summary>"1 commit", "3 commits".</summary>
    public static string Commits(int count) => count == 1 ? "1 commit" : string.Create(CultureInfo.InvariantCulture, $"{count} commits");

    private async Task<SourceCheck> ReadAsync(string root, string branch, string upstream, CancellationToken cancellationToken)
    {
        var head = await git.RunAsync(root, ["rev-parse", "@{upstream}"], cancellationToken).ConfigureAwait(false);
        var counts = await git.RunAsync(root, ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"], cancellationToken).ConfigureAwait(false);
        var status = await git.RunAsync(root, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=no"], cancellationToken).ConfigureAwait(false);
        if (head is not { ExitCode: 0 } || counts is not { ExitCode: 0 } || status is not { ExitCode: 0 }
            || counts.StandardOutput.Split('\t', StringSplitOptions.TrimEntries) is not [var aheadText, var behindText]
            || !int.TryParse(aheadText, NumberStyles.None, CultureInfo.InvariantCulture, out var ahead)
            || !int.TryParse(behindText, NumberStyles.None, CultureInfo.InvariantCulture, out var behind))
        {
            return SourceCheck.Failed($"Git couldn't compare the checkout with {upstream}.");
        }
        IReadOnlyList<UpstreamCommit> commits = [];
        if (behind > 0)
        {
            var log = await git.RunAsync(root, ["log", $"-n{MaxCommits}", "--format=%H%x1f%s%x1f%an%x1f%cI", "HEAD..@{upstream}"], cancellationToken).ConfigureAwait(false);
            commits = log is { ExitCode: 0 } ? ParseLog(log.StandardOutput) : [];
        }
        var hasChanges = status.StandardOutput.Trim().Length > 0;
        return new SourceCheck(new UpstreamStatus(branch, upstream, head.StandardOutput.Trim(), behind, ahead, hasChanges, commits), null);
    }

    /// <summary>Reads <c>git log --format=%H%x1f%s%x1f%an%x1f%cI</c>: a commit a line, its fields split by the unit separator.</summary>
    public static IReadOnlyList<UpstreamCommit> ParseLog(string output)
    {
        var commits = new List<UpstreamCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.TrimEnd('\r').Split(Separator) is [var hash, var subject, var author, var when]
                && DateTimeOffset.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                commits.Add(new UpstreamCommit(hash, subject, author, at));
            }
        }
        return commits;
    }

    private static string GitError(ProcessResult? result, string command) =>
        result is null ? $"{command} didn't finish."
        : (result.StandardError + result.StandardOutput).Trim() is { Length: > 0 } said ? said
        : string.Create(CultureInfo.InvariantCulture, $"{command} failed (exit code {result.ExitCode}).");
}
