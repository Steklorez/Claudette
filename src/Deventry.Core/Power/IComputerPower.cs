namespace Deventry.Core.Power;

/// <summary>What the computer does once Claude is done: the header cup's <b>When Claude is done</b> (DESIGN.md §9).</summary>
public enum WhenDone
{
    Nothing,

    /// <summary>Deventry closes, as from its window, and the computer shuts down.</summary>
    ShutDown,

    /// <summary>The computer goes to sleep; Deventry and its tabs stay as they are.</summary>
    Sleep,
}

/// <summary>
/// Shuts the computer down or puts it to sleep, for <b>When Claude is done</b> (DESIGN.md §9, "When Claude is done").
/// Neither is forced: an app with unsaved work can still hold a shutdown up. The implementations are in
/// Deventry.Platform: <c>shutdown.exe</c> and <c>SetSuspendState</c> or the display turning off on Windows, System Events
/// and <c>pmset</c> on macOS, <c>systemctl</c> on Linux.
/// </summary>
public interface IComputerPower
{
    /// <summary>Why Deventry can't do <paramref name="action"/> on this computer, or null when it can.</summary>
    string? UnavailableReason(WhenDone action);

    /// <summary>
    /// The user just turned <paramref name="action"/> on: what it needs asked for while they're there, such as macOS's
    /// permission for Deventry to tell System Events to shut down. Null when it's ready, or why it can't be.
    /// </summary>
    Task<string?> PrepareAsync(WhenDone action, CancellationToken cancellationToken = default);

    /// <summary>Shuts down or sleeps now. Null once the system has been asked, or why it couldn't be.</summary>
    Task<string?> RunAsync(WhenDone action, CancellationToken cancellationToken = default);
}

/// <summary>No way to shut the computer down or put it to sleep: tests, a demo, and systems Deventry doesn't know.</summary>
public sealed class NoComputerPower(string reason = "Deventry can't shut this computer down or put it to sleep.") : IComputerPower
{
    public string? UnavailableReason(WhenDone action) => reason;

    public Task<string?> PrepareAsync(WhenDone action, CancellationToken cancellationToken = default) => Task.FromResult<string?>(reason);

    public Task<string?> RunAsync(WhenDone action, CancellationToken cancellationToken = default) => Task.FromResult<string?>(reason);
}
