using Claudette.Core.Files;

namespace Claudette.App.Tests.Support;

/// <summary>
/// Stands in for the OS's trash (DESIGN.md §18, "Project files", "Renaming and deleting"): never the user's own. What it
/// takes goes into a folder of its own under the test's, as a trash would; with <see cref="Next"/> set, it does what that
/// says instead, and nothing moves.
/// </summary>
internal sealed class FakeTrash(string folder) : ITrash
{
    private readonly Lock _lock = new();
    private readonly List<string> _taken = [];

    public string Name => "the Recycle Bin";

    /// <summary>The paths moved to it, in order.</summary>
    public IReadOnlyList<string> Taken
    {
        get
        {
            lock (_lock)
            {
                return [.. _taken];
            }
        }
    }

    /// <summary>What the next move comes to instead: no trash, or a failure.</summary>
    public TrashResult? Next { get; set; }

    public Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Next is { } next)
        {
            Next = null;
            return Task.FromResult(next);
        }
        Directory.CreateDirectory(folder);
        var into = Path.Combine(folder, $"{Guid.NewGuid():N}-{Path.GetFileName(path)}");
        if (Directory.Exists(path))
        {
            Directory.Move(path, into);
        }
        else
        {
            File.Move(path, into);
        }
        lock (_lock)
        {
            _taken.Add(path);
        }
        return Task.FromResult(TrashResult.Trashed);
    }
}
