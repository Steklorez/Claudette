using System.Globalization;
using Claudette.App.Services;
using Claudette.Core;
using Claudette.Core.Development;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The sidebar's "3 new commits on origin/main" badge for a source build, its dialog, and the updates part of Settings →
/// General for one (DESIGN.md §9, "New commits upstream").
/// </summary>
public sealed partial class SourceUpdateViewModel : ObservableObject, IDisposable
{
    private readonly SourceUpdateService _updates;
    private readonly TimeProvider _time;

    public SourceUpdateViewModel(SourceUpdateService updates, TimeProvider time)
    {
        _updates = updates;
        _time = time;
        _updates.Changed += Refresh;
    }

    private UpstreamStatus? Status => _updates.Status;

    private string Upstream => Status?.Upstream ?? "its upstream";

    /// <summary>"3 new commits on origin/main".</summary>
    private string NewCommits => Status?.Behind is 1
        ? $"1 new commit on {Upstream}"
        : string.Create(CultureInfo.InvariantCulture, $"{Status?.Behind ?? 0} new commits on {Upstream}");

    private bool HasNewCommits => Status is { Behind: > 0 };

    public bool HasBadge => (HasNewCommits && !_updates.IsSkipped) || _updates.IsBusy || _updates.LastError is not null;

    public string BadgeText =>
        _updates.IsPulling ? $"Pulling {Upstream}…"
        : _updates.IsBuilding ? "Building Claudette…"
        : _updates.BuildFailed ? "Build failed"
        : !HasNewCommits && _updates.LastError is not null ? "Update didn't happen"
        : NewCommits;

    public string Title => _updates.BuildFailed ? "The build of the new commits failed"
        : HasNewCommits ? NewCommits
        : "Claudette's checkout";

    public string DetailText => Status is { } status
        ? $"The checkout in {_updates.RepositoryRoot} is on {status.Branch}, which tracks {status.Upstream}."
            + (_updates.CanBuild ? " Updating pulls the new commits, builds Claudette and restarts into it, with every tab as it was." : "")
        : $"The checkout in {_updates.RepositoryRoot}.";

    /// <summary>The newest of the new commits, newest first.</summary>
    public IReadOnlyList<SourceCommitRow> Commits => Status?.Commits.Select(c => new SourceCommitRow(c, _time)).ToArray() ?? [];

    public bool HasCommits => Commits.Count > 0;

    /// <summary>"and 12 more", when there are more new commits than are listed.</summary>
    public string? MoreText => Status is { } status && status.Behind > status.Commits.Count && status.Commits.Count > 0
        ? string.Create(CultureInfo.InvariantCulture, $"and {status.Behind - status.Commits.Count} more")
        : null;

    /// <summary>Why the commits can't be pulled here: the checkout's own work is in the way.</summary>
    public string? WhyNotText => _updates.IsBusy ? null : Status?.WhyNotPull;

    public string? InPlaceText => !_updates.CanBuild && HasNewCommits && Status?.CanPull == true
        ? "This build runs in place, so Claudette can't rebuild it while it runs. Pull, then build it yourself."
        : null;

    private bool CanPull => Status?.CanPull == true && !_updates.IsBusy;

    /// <summary>Pull, build and restart, and Pull and build.</summary>
    public bool CanUpdate => CanPull && _updates.CanBuild;

    public bool CanUpdateWhenIdle => CanUpdate && _updates.AnyTabWorking;

    /// <summary>Pull alone, for a build that runs in place.</summary>
    public bool CanPullOnly => CanPull && !_updates.CanBuild;

    public bool CanBuildAgain => _updates.BuildFailed && !_updates.IsBusy;

    public bool CanSkip => HasNewCommits && !_updates.IsSkipped && !_updates.IsBusy;

    public string? WorkingText => (CanUpdate || CanBuildAgain) && _updates.AnyTabWorking
        ? "A tab is working. Restarting interrupts it, and it resumes after the restart; Update when idle waits for it to finish."
        : null;

    public bool IsBusy => _updates.IsBusy;

    public string BuildOutput => string.Join('\n', _updates.BuildLines);

    public bool HasBuildOutput => _updates.BuildLines.Count > 0;

    public string? ErrorText => _updates.LastError;

    public bool HasError => ErrorText is not null;

    // ---- For Settings → General ---------------------------------------------------------------------------------

    public string CheckStatusText =>
        _updates.IsChecking ? $"Checking {Upstream} for new commits…"
        : _updates.CheckProblem is { } problem ? problem
        : _updates.LastChecked is not { } at || Status is null ? "Not checked yet."
        : !HasNewCommits ? $"Up to date with {Upstream}. Checked at {at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}."
        : _updates.IsSkipped ? $"{Formats.Capitalize(NewCommits)}; you skipped them."
        : $"{Formats.Capitalize(NewCommits)}.";

    public string SourceText => $"This is a source build, so instead of releases it checks the branch its checkout tracks for new commits, and restarts into each new build of {_updates.RepositoryRoot}.";

    public bool CanCheck => !_updates.IsChecking && !_updates.IsBusy;

    [RelayCommand]
    private Task CheckNowAsync() => _updates.CheckNowAsync();

    [RelayCommand]
    private Task UpdateAsync() => _updates.UpdateAsync(SourceRestart.Now);

    [RelayCommand]
    private Task UpdateWhenIdleAsync() => _updates.UpdateAsync(SourceRestart.WhenIdle);

    [RelayCommand]
    private Task PullAndBuildAsync() => _updates.UpdateAsync(SourceRestart.Later);

    [RelayCommand]
    private Task PullAsync() => _updates.UpdateAsync(SourceRestart.Later);

    [RelayCommand]
    private Task BuildAgainAsync() => _updates.BuildAgainAsync();

    [RelayCommand]
    private void Skip() => _updates.Skip();

    private void Refresh() => OnPropertyChanged(string.Empty);

    public void Dispose() => _updates.Changed -= Refresh;
}

/// <summary>A new commit in the dialog: its subject, and who made it and when.</summary>
public sealed class SourceCommitRow(UpstreamCommit commit, TimeProvider time)
{
    public string Hash => commit.ShortHash;

    public string Subject => commit.Subject;

    public string Detail => $"{commit.Author} · {Formats.Ago(time.GetUtcNow() - commit.When)}";
}
