namespace Claudette.Core.Protocol;

/// <summary>Where an image in a reply's Markdown is, which decides when it may load (DESIGN.md §18, "Images in Claude's replies").</summary>
public enum ReplyImageKind
{
    /// <summary>A <c>data:</c> image in the reply itself: it shows at once.</summary>
    Inline,

    /// <summary>A file in the tab's folder, with no link on the way to it: it shows at once.</summary>
    InFolder,

    /// <summary>A file elsewhere on this computer: it waits for a click.</summary>
    LocalFile,

    /// <summary>An <c>http</c> or <c>https</c> address: it waits for a click.</summary>
    Web,

    /// <summary>It never loads: a network path, another scheme, or something that isn't an address.</summary>
    Refused,
}

/// <summary>
/// An image's address in a reply, and where it points. A reply can be prompt-injected through anything Claude read, and
/// an image that loads by itself would let it send data out in the address, so only what can't reach another machine
/// loads at once: a <c>data:</c> image, or a file in the tab's folder. A file elsewhere could be on a path the system
/// mounts from the network by name (<c>/net/&lt;host&gt;</c> on macOS), so it waits for a click, as a web image does, and
/// deciding that never touches the file system outside the folder. A network path (<c>\\server\share</c>,
/// <c>file://server/…</c>) never loads: Windows would hand the server the user's credentials.
/// </summary>
/// <param name="Address">The address as the reply has it.</param>
/// <param name="Path">The full path, for a file.</param>
/// <param name="Url">The address, for a web image.</param>
/// <param name="Image">The image, for a <c>data:</c> one.</param>
/// <param name="Reason">Why it won't load, for one that's refused.</param>
public sealed record ReplyImageAddress(ReplyImageKind Kind, string Address, string? Path = null, Uri? Url = null, MessageImage? Image = null, string? Reason = null)
{
    /// <summary>It shows without a click.</summary>
    public bool LoadsAtOnce => Kind is ReplyImageKind.Inline or ReplyImageKind.InFolder;

    /// <summary>Where it's from, for its placeholder: <c>example.com</c>, or the file's path.</summary>
    public string Origin => Url?.Host ?? Path ?? Address;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Where <paramref name="address"/> points, read against the tab's <paramref name="folder"/>, or for a Markdown file
    /// previewed, against <paramref name="relativeTo"/>, the file's own folder (DESIGN.md §18, "Markdown preview"). Only a
    /// file in <paramref name="folder"/> loads at once either way.
    /// </summary>
    public static ReplyImageAddress Classify(string address, string folder, string? relativeTo = null)
    {
        var text = address.Trim();
        if (text.Length == 0)
        {
            return Refused(address, "It has no address.");
        }
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return FromData(address, text);
        }
        if (IsNetworkPath(text))
        {
            return Refused(address, NetworkReason);
        }
        string path;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                return new ReplyImageAddress(ReplyImageKind.Web, address, Url: uri);
            }
            if (!uri.IsFile)
            {
                return Refused(address, $"\"{uri.Scheme}:\" images don't load.");
            }
            if (uri.IsUnc || uri.Host.Length > 0 && !uri.IsLoopback)
            {
                return Refused(address, NetworkReason);
            }
            path = uri.LocalPath;
        }
        else
        {
            // A Markdown link can't hold a space, so a path with one comes percent-encoded: my%20chart.png.
            path = text.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(text) : text;
        }
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(System.IO.Path.Combine(relativeTo ?? folder, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Refused(address, "It isn't a path or address Claudette can load.");
        }
        if (IsNetworkPath(full))
        {
            return Refused(address, NetworkReason);
        }
        return Locate(full, folder) switch
        {
            ReplyImageKind.Refused => Refused(address, NetworkReason),
            var kind => new ReplyImageAddress(kind, address, Path: full),
        };
    }

    private const string NetworkReason = "Images in network folders don't load.";

    private static ReplyImageAddress Refused(string address, string reason) => new(ReplyImageKind.Refused, address, Reason: reason);

    /// <summary><c>data:image/png;base64,…</c>: the image, when it's one the app can show.</summary>
    private static ReplyImageAddress FromData(string address, string text)
    {
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        var header = comma > 5 ? text[5..comma] : "";
        const string Base64 = ";base64";
        // Its bytes say what it is, whatever the header claims.
        if (header.EndsWith(Base64, StringComparison.OrdinalIgnoreCase)
            && MessageImage.FromBase64(null, text[(comma + 1)..]) is { } image
            && ViewerImages.DetectMediaType(image.Data) is { } mediaType)
        {
            return new ReplyImageAddress(ReplyImageKind.Inline, address, Image: image with { MediaType = mediaType });
        }
        return Refused(address, ViewerImages.NotAnImage);
    }

    /// <summary><c>\\server\share</c> or <c>//server/share</c>, which Windows reaches over the network.</summary>
    private static bool IsNetworkPath(string path) =>
        path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/';

    /// <summary>
    /// <see cref="ReplyImageKind.InFolder"/> when <paramref name="full"/> is inside <paramref name="folder"/> as spelled and
    /// nothing on the way down from the folder is a link, which could lead anywhere; else
    /// <see cref="ReplyImageKind.LocalFile"/>, or <see cref="ReplyImageKind.Refused"/> for a link in the folder to a
    /// network path. Only paths inside the folder are looked at, and a link only for where it says it goes.
    /// </summary>
    private static ReplyImageKind Locate(string full, string folder)
    {
        string root;
        try
        {
            root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ReplyImageKind.LocalFile;
        }
        var prefix = root + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison))
        {
            return ReplyImageKind.LocalFile;
        }
        var current = root;
        foreach (var part in full[prefix.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            try
            {
                if (new FileInfo(current).LinkTarget is { } target)
                {
                    return IsNetworkPath(target) ? ReplyImageKind.Refused : ReplyImageKind.LocalFile;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ReplyImageKind.LocalFile;
            }
        }
        return ReplyImageKind.InFolder;
    }
}
