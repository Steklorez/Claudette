using Claudette.Core.Protocol;

namespace Claudette.App.Conversation;

/// <summary>
/// A tab's images in replies (DESIGN.md §18, "Images in Claude's replies"): where each points, read against the folder
/// Claude works in, which ones the user let load, and the last ones loaded, so a reply built again as it scrolls back into
/// view shows them at once rather than asking again.
/// </summary>
public sealed class ReplyImages(ReplyImageLoader loader, Func<string> folder)
{
    /// <summary>How many loaded images are kept.</summary>
    internal const int Kept = 32;

    private readonly Lock _lock = new();
    private readonly HashSet<string> _allowed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MessageImage> _loaded = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public ReplyImageAddress Classify(string address) => ReplyImageAddress.Classify(address, folder());

    /// <summary>The user loaded it once already in this tab, so it loads by itself from now on.</summary>
    public bool IsAllowed(string address)
    {
        lock (_lock)
        {
            return _allowed.Contains(address);
        }
    }

    /// <summary>The image, when it's one of the last ones loaded.</summary>
    public MessageImage? Loaded(string address)
    {
        lock (_lock)
        {
            return _loaded.GetValueOrDefault(address);
        }
    }

    /// <summary>Loads it, and remembers it was let load; throws <see cref="ReplyImageException"/> when it can't.</summary>
    public async Task<MessageImage> LoadAsync(ReplyImageAddress address, CancellationToken cancellationToken = default)
    {
        var image = await loader.LoadAsync(address, cancellationToken);
        lock (_lock)
        {
            _allowed.Add(address.Address);
            if (_loaded.TryAdd(address.Address, image))
            {
                _order.Enqueue(address.Address);
                while (_order.Count > Kept)
                {
                    _loaded.Remove(_order.Dequeue());
                }
            }
        }
        return image;
    }
}
