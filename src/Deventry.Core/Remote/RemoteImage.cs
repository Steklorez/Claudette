namespace Deventry.Core.Remote;

/// <summary>
/// An image the agent made in a turn, ready for a client to send after the turn's message, when its page in Settings
/// says to (DESIGN.md §18, "Remote clients", "Images"): a PNG or a JPEG, no larger than <see cref="MaxBytes"/> or
/// <see cref="MaxSide"/> pixels across.
/// </summary>
/// <param name="Name">Its name, with its kind's extension: <c>chart.png</c>, or <c>Screenshot.png</c> for one with no file.</param>
/// <param name="MediaType"><c>image/png</c> or <c>image/jpeg</c>.</param>
public sealed record RemoteImage(string Name, string MediaType, byte[] Data, int Width, int Height)
{
    /// <summary>The most a turn sends: one album, as Telegram and Discord each take ten in one message.</summary>
    public const int MostPerTurn = 10;

    /// <summary>The largest an image is sent: what Telegram takes as a photo and Discord as a bot's file.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>The most pixels across either way: a larger image is scaled down to it, as a phone shows no more.</summary>
    public const int MaxSide = 4096;
}
