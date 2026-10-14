using System.Buffers;
using Claudette.Core.Composer;

namespace Claudette.Core.Protocol;

/// <summary>Why an image in a reply didn't load, in words to show.</summary>
public sealed class ReplyImageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Loads an image in a reply (DESIGN.md §18, "Images in Claude's replies") once <see cref="ReplyImageAddress"/> says it
/// may: a file from the disk, or a web image through Claudette's own client, never LiveMarkdown's. Either is at most
/// <see cref="MaxBytes"/> and must be a PNG, JPEG, GIF or WebP by its first bytes, whatever its name or the server says.
/// </summary>
public sealed class ReplyImageLoader(HttpClient http, TimeProvider time)
{
    /// <summary>The largest image loaded, as for an attachment.</summary>
    public const long MaxBytes = Attachments.MaxImageBytes;

    /// <summary>How long a web image has to arrive.</summary>
    public static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(30);

    private const int BufferSize = 81920;

    private static readonly string TooBig = $"It's over {MaxBytes / (1024 * 1024)} MB.";

    /// <summary>Loads the image <paramref name="address"/> points to; throws <see cref="ReplyImageException"/> when it can't.</summary>
    public async Task<MessageImage> LoadAsync(ReplyImageAddress address, CancellationToken cancellationToken = default) => address.Kind switch
    {
        ReplyImageKind.Inline => address.Image!,
        ReplyImageKind.InFolder or ReplyImageKind.LocalFile => await Task.Run(() => ReadFile(address.Path!), cancellationToken).ConfigureAwait(false),
        ReplyImageKind.Web => await DownloadAsync(address.Url!, cancellationToken).ConfigureAwait(false),
        _ => throw new ReplyImageException(address.Reason ?? "It doesn't load."),
    };

    private static MessageImage ReadFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new ReplyImageException("There's no such file.");
            }
            if (file.Length > MaxBytes)
            {
                throw new ReplyImageException(TooBig);
            }
            return Image(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ReplyImageException($"It couldn't be read: {ex.Message}", ex);
        }
    }

    private async Task<MessageImage> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(WebTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ReplyImageException($"{url.Host} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new ReplyImageException(TooBig);
            }
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            return Image(await ReadUpToAsync(stream, linked.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ReplyImageException($"{url.Host} took too long to answer.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new ReplyImageException($"{url.Host} couldn't be reached: {ex.Message}", ex);
        }
    }

    /// <summary>The whole stream, stopping as soon as it's over <see cref="MaxBytes"/>, whatever the server said its length was.</summary>
    private static async Task<byte[]> ReadUpToAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (data.Length + read > MaxBytes)
                {
                    throw new ReplyImageException(TooBig);
                }
                data.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return data.ToArray();
    }

    private static MessageImage Image(byte[] bytes) =>
        MessageImage.DetectMediaType(bytes) is { } mediaType
            ? new MessageImage(mediaType, bytes)
            : throw new ReplyImageException("It isn't a PNG, JPEG, GIF or WebP image.");
}
