namespace Claudette.Core.Perforce;

/// <summary>
/// Finds and remembers the Perforce workspace of each folder, for Perforce in the Files tab and the automatic actions
/// (DESIGN.md §18, "Finding the workspace"). It runs in the background and never holds up a tab: whoever asks while it
/// runs shares it. A folder that isn't in a workspace is remembered until <see cref="Forget"/> (the explorer's
/// <b>Refresh</b>); one whose server didn't answer is asked again on the next request after <see cref="RetryAfter"/>.
/// Thread-safe.
/// </summary>
public sealed class PerforceWorkspaceFinder(PerforceClient client, TimeProvider timeProvider)
{
    /// <summary>A server that didn't answer is asked again once this long has passed, when something needs it.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly Dictionary<PerforceTarget, (Task<PerforceDetection> Finding, DateTimeOffset StartedAt)> _found = [];

    /// <summary>The folder's workspace, found now or before; null when it isn't in one.</summary>
    public async Task<PerforceWorkspace?> FindAsync(PerforceTarget target, CancellationToken cancellationToken = default) =>
        (await DetectAsync(target, cancellationToken).ConfigureAwait(false)).Workspace;

    /// <summary>What finding the folder's workspace came to, found now or before.</summary>
    public Task<PerforceDetection> DetectAsync(PerforceTarget target, CancellationToken cancellationToken = default)
    {
        var key = Key(target);
        Task<PerforceDetection> finding;
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (!_found.TryGetValue(key, out var found) || IsStale(found.Finding, found.StartedAt, now))
            {
                found = (Task.Run(() => client.FindWorkspaceAsync(target)), now);
                _found[key] = found;
            }
            finding = found.Finding;
        }
        return finding.WaitAsync(cancellationToken);
    }

    /// <summary>The folder's workspace when finding it has finished; null while it runs, or when there's none.</summary>
    public PerforceWorkspace? Known(PerforceTarget target)
    {
        lock (_lock)
        {
            return _found.TryGetValue(Key(target), out var found) && found.Finding.IsCompletedSuccessfully ? found.Finding.Result.Workspace : null;
        }
    }

    /// <summary>Ticket handling found the folder's workspace before the tab started: no need to ask again.</summary>
    public void Remember(PerforceTarget target, PerforceWorkspace workspace)
    {
        lock (_lock)
        {
            _found[Key(target)] = (Task.FromResult(new PerforceDetection(PerforceDetectionOutcome.Workspace, workspace)), timeProvider.GetUtcNow());
        }
    }

    /// <summary>Forgets what was found for the folder, so the next request asks again.</summary>
    public void Forget(PerforceTarget target)
    {
        lock (_lock)
        {
            _found.Remove(Key(target));
        }
    }

    private static bool IsStale(Task<PerforceDetection> finding, DateTimeOffset startedAt, DateTimeOffset now) =>
        finding.IsCompleted && (!finding.IsCompletedSuccessfully || finding.Result.Outcome == PerforceDetectionOutcome.Unreachable && now - startedAt >= RetryAfter);

    /// <summary>The folder as one key whichever way its path is written: full, without a separator at its end.</summary>
    private static PerforceTarget Key(PerforceTarget target)
    {
        string folder;
        try
        {
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            folder = target.Folder;
        }
        if (!OperatingSystem.IsLinux())
        {
            folder = folder.ToUpperInvariant();
        }
        return target with { Folder = folder };
    }
}
