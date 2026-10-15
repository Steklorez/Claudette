using Claudette.Core.Protocol;

namespace Claudette.App.Conversation;

/// <summary>
/// The files a tab's replies name (DESIGN.md §18, "Files named in Claude's replies"): which inline code is a file, and
/// where a clicked link's file is. What inline code was found to name is remembered, so a reply built again as it
/// scrolls back into view shows its links at once; it doesn't outlive the tab.
/// </summary>
/// <param name="finder">Looks for the files.</param>
/// <param name="sessionFiles">The files the session's tools read or changed, the latest last; read on the UI thread.</param>
public sealed class ReplyFiles(ReplyFileFinder finder, Func<IReadOnlyList<string>> sessionFiles)
{
    /// <summary>At most this many names are remembered; past it, they're forgotten and found again.</summary>
    public const int Kept = 512;

    private readonly Dictionary<string, IReadOnlyList<string>> _found = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>What inline code with this text was found to name before; null when it wasn't looked for or nothing was found.</summary>
    public IReadOnlyList<string>? Found(string text)
    {
        lock (_lock)
        {
            return _found.GetValueOrDefault(text);
        }
    }

    /// <summary>
    /// The files and folders <paramref name="reference"/> names, the likeliest first. Inline code looks only where that
    /// can't reach another machine; a click (<paramref name="clicked"/>) looks wherever it points (<see cref="ReplyFileFinder"/>).
    /// </summary>
    public async Task<IReadOnlyList<string>> FindAsync(ReplyFileReference reference, bool clicked, CancellationToken cancellationToken = default)
    {
        var found = await finder.FindAsync(reference, [.. sessionFiles()], clicked, cancellationToken);
        if (!clicked && found.Count > 0)
        {
            lock (_lock)
            {
                if (_found.Count >= Kept)
                {
                    _found.Clear();
                }
                _found[reference.Text] = found;
            }
        }
        return found;
    }
}
