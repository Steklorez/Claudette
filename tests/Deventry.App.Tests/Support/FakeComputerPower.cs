using Deventry.Core.Power;

namespace Deventry.App.Tests.Support;

/// <summary>
/// Stands in for shutting the computer down or putting it to sleep (DESIGN.md §9, "When Claude is done"): it only keeps
/// what it was asked. Never the real one, which would shut the computer running the tests down.
/// </summary>
internal sealed class FakeComputerPower : IComputerPower
{
    private readonly List<WhenDone> _runs = [];
    private readonly List<WhenDone> _prepared = [];

    /// <summary>Why each can't be done here; none by default.</summary>
    public Dictionary<WhenDone, string> Unavailable { get; } = [];

    /// <summary>What turning one on is told, such as macOS's Automation permission refused; null by default.</summary>
    public string? PrepareAnswer { get; set; }

    /// <summary>What doing it is told; null, asked, by default.</summary>
    public string? RunAnswer { get; set; }

    /// <summary>Runs as each is done: what else was so then, such as sleep being held off.</summary>
    public Action<WhenDone>? OnRun { get; set; }

    public IReadOnlyList<WhenDone> Runs
    {
        get
        {
            lock (_runs)
            {
                return [.. _runs];
            }
        }
    }

    public IReadOnlyList<WhenDone> Prepared
    {
        get
        {
            lock (_runs)
            {
                return [.. _prepared];
            }
        }
    }

    public string? UnavailableReason(WhenDone action) => Unavailable.GetValueOrDefault(action);

    public Task<string?> PrepareAsync(WhenDone action, CancellationToken cancellationToken = default)
    {
        lock (_runs)
        {
            _prepared.Add(action);
        }
        return Task.FromResult(PrepareAnswer);
    }

    public Task<string?> RunAsync(WhenDone action, CancellationToken cancellationToken = default)
    {
        OnRun?.Invoke(action);
        lock (_runs)
        {
            _runs.Add(action);
        }
        return Task.FromResult(RunAnswer);
    }
}
