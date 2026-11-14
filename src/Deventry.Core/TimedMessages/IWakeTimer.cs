namespace Deventry.Core.TimedMessages;

/// <summary>What setting up waking this computer takes (DESIGN.md §18, "Timed messages", "Waking the computer").</summary>
public enum WakeSetup
{
    /// <summary>Nothing: Windows lets any program wake the computer.</summary>
    NotNeeded,

    /// <summary>The wake helper, installed once with an administrator's password: macOS and Linux.</summary>
    NeedsHelper,

    /// <summary>The wake helper is installed.</summary>
    HelperInstalled,

    /// <summary>This computer can't be woken by Deventry at all (<see cref="IWakeTimer.UnavailableReason"/>).</summary>
    Unavailable,
}

/// <summary>
/// Wakes the computer from sleep at a time, for the next timed message (DESIGN.md §18, "Timed messages", "Waking the
/// computer"). One wake at a time: setting another moves it. The implementations are in Deventry.Platform: a waitable
/// timer on Windows, and a helper the user installs on macOS (<c>pmset</c>) and Linux (the RTC alarm).
/// </summary>
public interface IWakeTimer : IDisposable
{
    /// <summary>What it takes here, as Settings says.</summary>
    WakeSetup Setup { get; }

    /// <summary>Why this computer can't be woken at all, or null when it can, or could once the helper is installed.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Why a wake may not happen though one is set, or null: Windows' power plan not allowing wake timers, on battery or
    /// at all. Read again each time it's asked.
    /// </summary>
    string? Caveat { get; }

    /// <summary>When the computer is to wake, or null when no wake is set.</summary>
    DateTimeOffset? WakesAt { get; }

    /// <summary>
    /// Wakes the computer at <paramref name="at"/>, in place of any wake set before, or sets none with null. A time
    /// that's gone by sets none. Never throws: what fails is in <see cref="Describe"/>.
    /// </summary>
    Task WakeAtAsync(DateTimeOffset? at, CancellationToken cancellationToken = default);

    /// <summary>Installs the wake helper, asking for an administrator's password; whether it's installed after.</summary>
    Task<bool> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the wake helper, asking for the password again; whether it's gone after.</summary>
    Task<bool> UninstallAsync(CancellationToken cancellationToken = default);

    /// <summary>What it's doing, in words, for Settings → Advanced → Diagnostics.</summary>
    string Describe();
}

/// <summary>No way to wake this computer: tests, and systems without one.</summary>
public sealed class NoWakeTimer(string reason = "Deventry can't wake this computer.") : IWakeTimer
{
    public WakeSetup Setup => WakeSetup.Unavailable;

    public string? UnavailableReason => reason;

    public string? Caveat => null;

    public DateTimeOffset? WakesAt => null;

    public Task WakeAtAsync(DateTimeOffset? at, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> InstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> UninstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public string Describe() => $"Not available: {reason}";

    public void Dispose()
    {
    }
}
