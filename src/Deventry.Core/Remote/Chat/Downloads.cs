namespace Deventry.Core.Remote.Chat;

/// <summary>A file downloaded from a chat service, read no further than it's allowed to be (DESIGN.md §18, "Attachments from a client").</summary>
internal static class Downloads
{
    /// <summary>
    /// The answer's body, or null when it's more than <paramref name="maxBytes"/>: said so by its length, or found so
    /// as it's read, without reading the rest.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
        {
            return null;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maxBytes)
            {
                return null;
            }
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}
