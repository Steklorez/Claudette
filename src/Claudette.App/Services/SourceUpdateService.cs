using Claudette.Core.Development;
using Claudette.Core.Diffs;
using Microsoft.Extensions.Logging;

namespace Claudette.App.Services;

/// <summary>
/// For a source build of Claudette (DESIGN.md §9, "New commits upstream"): checks the branch its checkout tracks for new
/// commits at launch and every hour, as an installed Claudette checks its releases, and pulls and builds them. The build
/// goes to <see cref="RestartService"/>, which restarts into it with every tab as it was.
/// </summary>
public sealed class SourceUpdateService : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly AppServices _services;
    private readonly RestartService _restarts;
    private readonly SourceCheckout _checkout;
    private readonly SourceBuild? _source;
    private readonly ILogger _logger;
    private readonly Lock _checkLock = new();
    private ITimer? _timer;
    private Task? _checking;
    private bool _disposed;

    /// <summary>When the last update was to restart, for Build again.</summary>
    private SourceRestart _restart;

    /// <param name="checkout">For tests: git and <c>dotnet</c> through a fake launcher.</param>
    public SourceUpdateService(AppServices services, RestartService restarts, SourceCheckout? checkout = null)
    {
        _services = services;
        _restarts = restarts;
        _checkout = checkout ?? new SourceCheckout(services.Git, services.Launcher, services.Time, services.UserEnvironment);
        _source = SourceBuild.Detect(restarts.Build.SourceOutput);
        _logger = services.Loggers.CreateLogger<SourceUpdateService>();
        _restarts.Changed += OnRestartsChanged;
    }

    /// <summary>The checkout this build was built from.</summary>
    public string? RepositoryRoot => _source?.RepositoryRoot;

    /// <summary>
    /// This build runs from a copy, so its build output can be rebuilt while it runs. One that runs in place can only
    /// be pulled.
    /// </summary>
    public bool CanBuild => _source is not null && !ChangedFiles.PlatformPathComparer.Equals(
        Path.TrimEndingDirectorySeparator(_restarts.Build.RunningDirectory), Path.TrimEndingDirectorySeparator(_restarts.Build.SourceOutput));

    /// <summary>How the checkout stood against its upstream at the last check.</summary>
    public UpstreamStatus? Status { get; private set; }

    /// <summary>Why the last check found nothing to go on, or null.</summary>
    public string? CheckProblem { get; private set; }

    public bool IsChecking { get; private set; }

    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Whether the user skipped the commits <see cref="Status"/> has: they aren't announced, but Settings shows them.</summary>
    public bool IsSkipped => Status is { Behind: > 0 } status && _services.State.SkippedSourceUpdate == status.UpstreamHead;

    public bool IsPulling { get; private set; }

    public bool IsBuilding { get; private set; }

    /// <summary>The build's last lines, while it runs and after it failed.</summary>
    public IReadOnlyList<string> BuildLines { get; private set; } = [];

    /// <summary>Why the last pull or build didn't happen, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>The checkout was pulled but its build failed, so it can be built again.</summary>
    public bool BuildFailed { get; private set; }

    public bool IsBusy => IsPulling || IsBuilding;

    public bool AnyTabWorking => _restarts.AnyTabWorking;

    /// <summary>Raised on the UI thread when anything above changes, or a tab's status does.</summary>
    public event Action? Changed;

    /// <summary>Starts checking at launch and every hour, if Settings allows it.</summary>
    public void Start() => OnSettingsChanged();

    public void OnSettingsChanged()
    {
        if (!_disposed && _source is not null && _services.Settings.General.CheckForAppUpdates)
        {
            _timer ??= _services.Time.CreateTimer(_ => _ = CheckNowAsync(), null, TimeSpan.Zero, CheckInterval);
        }
        else
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>For tests: the check under way or last run, to wait for it to finish.</summary>
    internal Task? LastCheck
    {
        get
        {
            lock (_checkLock)
            {
                return _checking;
            }
        }
    }

    /// <summary>Fetches and checks now. A check already under way is shared rather than started twice.</summary>
    public Task CheckNowAsync()
    {
        if (_source is null)
        {
            return Task.CompletedTask;
        }
        lock (_checkLock)
        {
            return _checking is { IsCompleted: false } running ? running : _checking = RunCheckAsync(_source.RepositoryRoot);
        }
    }

    private async Task RunCheckAsync(string root)
    {
        _services.Dispatcher.Post(() =>
        {
            IsChecking = true;
            Changed?.Invoke();
        });
        var check = await _checkout.CheckAsync(root).ConfigureAwait(false);
        if (check.Problem is { } problem)
        {
            _logger.LogInformation("Checking the checkout for new commits: {Problem}", problem);
        }
        _services.Dispatcher.Post(() =>
        {
            IsChecking = false;
            LastChecked = _services.Time.GetUtcNow();
            CheckProblem = check.Problem;
            // A failed check keeps what the last one found: the commits are still there to pull.
            Status = check.Status ?? Status;
            Changed?.Invoke();
        });
    }

    /// <summary>
    /// Pulls the new commits and builds them. Then <paramref name="restart"/> restarts into the build now or once no tab
    /// is working; null leaves it to be offered as any new build is. Returns false if it didn't get that far, with the
    /// reason in <see cref="LastError"/>.
    /// </summary>
    public async Task<bool> UpdateAsync(SourceRestart restart)
    {
        if (IsBusy || _source is not { } source)
        {
            return false;
        }
        IsPulling = true;
        LastError = null;
        BuildFailed = false;
        BuildLines = [];
        Changed?.Invoke();
        string? error;
        try
        {
            error = await Task.Run(() => _checkout.PullAsync(source.RepositoryRoot));
        }
        finally
        {
            IsPulling = false;
        }
        if (error is not null)
        {
            LastError = error;
            Changed?.Invoke();
            _ = CheckNowAsync();
            return false;
        }
        Status = Status is { } status ? status with { Behind = 0, Commits = [] } : null;
        Changed?.Invoke();
        _ = CheckNowAsync();
        return !CanBuild || await BuildAsync(restart);
    }

    /// <summary>Builds the checkout as it is, after a build that failed, and restarts as that update was to.</summary>
    public Task<bool> BuildAgainAsync() => IsBusy ? Task.FromResult(false) : BuildAsync(_restart);

    private async Task<bool> BuildAsync(SourceRestart restart)
    {
        if (_source is not { } source || !CanBuild)
        {
            return false;
        }
        _restart = restart;
        IsBuilding = true;
        LastError = null;
        BuildFailed = false;
        BuildLines = [];
        Changed?.Invoke();
        var lines = new Queue<string>();
        SourceBuildResult result;
        try
        {
            result = await Task.Run(() => _checkout.BuildAsync(source, line => _services.Dispatcher.Post(() =>
            {
                lines.Enqueue(line);
                while (lines.Count > SourceCheckout.BuildLines)
                {
                    lines.Dequeue();
                }
                if (IsBuilding)
                {
                    BuildLines = [.. lines];
                    Changed?.Invoke();
                }
            })));
        }
        finally
        {
            IsBuilding = false;
        }
        BuildLines = result.LastLines;
        if (!result.Succeeded)
        {
            _logger.LogWarning("Building the pulled checkout failed: {Error}", result.Error);
            LastError = result.Error;
            BuildFailed = true;
            Changed?.Invoke();
            return false;
        }
        BuildLines = [];
        Changed?.Invoke();
        if (BuildOutput.LatestWrite(source.OutputDirectory) is not { } written || written <= _restarts.Build.RunningStamp)
        {
            LastError = "The build is the one already running: the new commits didn't change Claudette.";
            Changed?.Invoke();
            return false;
        }
        if (restart != SourceRestart.Later)
        {
            _restarts.RestartIntoNewBuild(whenIdle: restart == SourceRestart.WhenIdle);
        }
        return true;
    }

    /// <summary>Hides the badge for these commits, until more are pushed.</summary>
    public void Skip()
    {
        if (Status is { Behind: > 0 } status)
        {
            _services.State.SkippedSourceUpdate = status.UpstreamHead;
            _services.SaveState();
            Changed?.Invoke();
        }
    }

    private void OnRestartsChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _disposed = true;
        _restarts.Changed -= OnRestartsChanged;
        _timer?.Dispose();
    }
}

/// <summary>When to restart into a build of new commits.</summary>
public enum SourceRestart
{
    /// <summary>As Restart now does.</summary>
    Now,

    /// <summary>Once no tab is starting, working or waiting on the user.</summary>
    WhenIdle,

    /// <summary>Offered as any new build is, or restarted into by itself as Settings says.</summary>
    Later,
}
