using Deventry.Core.TimedMessages;

namespace Deventry.App.Tests.Support;

/// <summary>Stands in for waking the computer for a timed message (DESIGN.md §18, "Timed messages"): never the real one.</summary>
internal sealed class FakeWakeTimer : IWakeTimer
{
    private readonly List<DateTimeOffset?> _wakes = [];

    public WakeSetup Setup { get; set; } = WakeSetup.NotNeeded;

    public string? UnavailableReason { get; set; }

    public string? Caveat { get; set; }

    public DateTimeOffset? WakesAt { get; private set; }

    /// <summary>Each wake asked for, null for none, in order.</summary>
    public IReadOnlyList<DateTimeOffset?> Wakes
    {
        get
        {
            lock (_wakes)
            {
                return [.. _wakes];
            }
        }
    }

    public bool IsDisposed { get; private set; }

    public int Installs { get; private set; }

    public Task WakeAtAsync(DateTimeOffset? at, CancellationToken cancellationToken = default)
    {
        lock (_wakes)
        {
            _wakes.Add(at);
            WakesAt = at;
        }
        return Task.CompletedTask;
    }

    public Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        Installs++;
        Setup = WakeSetup.HelperInstalled;
        return Task.FromResult(true);
    }

    public Task<bool> UninstallAsync(CancellationToken cancellationToken = default)
    {
        Setup = WakeSetup.NeedsHelper;
        return Task.FromResult(true);
    }

    public string Describe() => WakesAt is { } at ? $"Wakes at {at:O}." : "No wake set.";

    public void Dispose() => IsDisposed = true;
}
