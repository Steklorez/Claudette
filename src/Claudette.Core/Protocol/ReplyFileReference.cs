using System.Text.RegularExpressions;

namespace Claudette.Core.Protocol;

/// <summary>
/// A file a reply names (DESIGN.md §18, "Files named in Claude's replies"): in inline code (<c>`weapons.md`</c>,
/// <c>`docs/specs/weapons.md`</c>, <c>`Player.cpp:42`</c>) or as a Markdown link's address
/// (<c>[weapons.md](docs/specs/weapons.md#L42)</c>). It's the path as written, with any line number taken off: where it
/// is on disk is <see cref="ReplyFileFinder"/>'s to work out.
/// </summary>
/// <param name="Text">What the reply wrote.</param>
/// <param name="Path">The path, without a line number or fragment; a link's is unescaped.</param>
/// <param name="Line">The line it points at, when it says.</param>
public sealed partial record ReplyFileReference(string Text, string Path, int? Line = null)
{
    /// <summary>
    /// Inline code that names a file or folder: a path, or a name with an extension. Code such as <c>Foo.Bar()</c>,
    /// <c>git status</c> or <c>e.g.</c> isn't one; whether a name like <c>System.IO.Path</c> is a file is for
    /// <see cref="ReplyFileFinder"/> to find out. Null when it can't be.
    /// </summary>
    public static ReplyFileReference? FromCode(string code)
    {
        var text = code.Trim();
        if (text.Length is 0 or > MaxLength || text.Any(c => char.IsControl(c) || BadCharacters.Contains(c)) || IsNetworkPath(text))
        {
            return null;
        }
        var (path, line) = WithoutLine(text);
        if (FilePath(path) is not { } local || !LooksLikeFile(local))
        {
            return null;
        }
        return new ReplyFileReference(code, local, line);
    }

    /// <summary>
    /// A Markdown link's address that points at a file: a path, relative to the tab's folder or not, or a <c>file:</c>
    /// address on this computer. A fragment (<c>#L42</c>, <c>#heading</c>) is taken off, and a path is unescaped
    /// (<c>my%20notes.md</c>). Null for a web address, another scheme or a network path.
    /// </summary>
    public static ReplyFileReference? FromLink(string address)
    {
        var text = address.Trim();
        if (text.Length is 0 or > MaxLength * 3 || IsNetworkPath(text))
        {
            return null;
        }
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        var fragment = hash < 0 ? "" : text[hash..];
        var raw = hash < 0 ? text : text[..hash];
        string unescaped;
        try
        {
            unescaped = raw.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(raw) : raw;
        }
        catch (UriFormatException)
        {
            return null;
        }
        if (unescaped.Length == 0 || unescaped.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '|') || IsNetworkPath(unescaped))
        {
            return null;
        }
        var (path, line) = WithoutLine(unescaped + (LinePattern().IsMatch(fragment) ? fragment : ""));
        return FilePath(path) is { } local ? new ReplyFileReference(address, local, line) : null;
    }

    /// <summary>
    /// Something the OS would run or install rather than show: a program, a script, a shortcut or an installer. A click
    /// shows it in Finder or Explorer instead of opening it, since a reply could be prompt-injected to point at one
    /// under an innocent name. <paramref name="isExecutable"/> says a file without an extension has its execute bit set,
    /// which on macOS and Linux makes it a program.
    /// </summary>
    public static bool IsProgram(string path, bool isExecutable = false)
    {
        var extension = System.IO.Path.GetExtension(System.IO.Path.TrimEndingDirectorySeparator(path));
        return extension.Length > 1 ? ProgramExtensions.Contains(extension[1..]) : isExecutable;
    }

    /// <summary>
    /// Extensions the OSes run, install or follow rather than show: Windows' programs, scripts and shortcuts, macOS's
    /// apps and scripts, Linux's launchers and packages, and the scripting languages an installer may have tied to their
    /// interpreter.
    /// </summary>
    public static IReadOnlySet<string> ProgramExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Windows: programs, scripts, shortcuts and installers.
        "exe", "com", "scr", "pif", "cpl", "msc", "msi", "msp", "msix", "msixbundle", "appx", "appxbundle", "appinstaller",
        "application", "appref-ms", "gadget", "hta", "bat", "cmd", "ps1", "psm1", "vbs", "vbe", "js", "jse", "wsf", "wsh",
        "ws", "sct", "lnk", "url", "website", "scf", "reg", "inf", "settingcontent-ms", "library-ms", "search-ms", "diagcab",
        // macOS: apps, scripts, installers and links.
        "app", "command", "tool", "terminal", "workflow", "action", "scpt", "scptd", "applescript", "pkg", "mpkg", "webloc",
        "inetloc", "fileloc", "prefpane", "kext",
        // Linux: launchers, scripts and packages.
        "desktop", "appimage", "run", "sh", "bash", "zsh", "ksh", "csh", "fish", "deb", "rpm", "flatpakref", "snap",
        // Interpreters an installer may have made the default app.
        "py", "pyw", "pyz", "pyc", "rb", "rbw", "pl", "php", "lua", "tcl", "jar", "ahk", "au3",
    };

    private const int MaxLength = 400;

    /// <summary>Characters no file name has on Windows, so code with them is something else (<c>a | b</c>, <c>&lt;T&gt;</c>).</summary>
    private static readonly HashSet<char> BadCharacters = ['<', '>', '"', '|', '?', '*', '`'];

    /// <summary><c>\\server\share</c> or <c>//server/share</c>, which Windows reaches over the network.</summary>
    private static bool IsNetworkPath(string path) =>
        path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/';

    /// <summary>The path without a line number: <c>Player.cpp:42</c>, <c>:42:7</c>, <c>:42-51</c>, <c>#L42</c>, <c>#L42-L51</c>.</summary>
    private static (string Path, int? Line) WithoutLine(string text) =>
        LinePattern().Match(text) is { Success: true } match && match.Index > 0
            ? (text[..match.Index], int.TryParse(match.Groups["line"].ValueSpan, out var line) && line > 0 ? line : null)
            : (text, null);

    [GeneratedRegex(@"(?::(?<line>\d{1,7})(?:[:-]\d{1,7})?|#L(?<line>\d{1,7})(?:C\d+)?(?:-L?\d{1,7}(?:C\d+)?)?)$")]
    private static partial Regex LinePattern();

    /// <summary>
    /// The path in a reference: itself, or a <c>file:</c> address's on this computer. Null for any other scheme
    /// (<c>javascript:</c>, <c>vscode:</c>) or a <c>file:</c> address on another machine. A drive letter (<c>C:\</c>)
    /// isn't a scheme.
    /// </summary>
    private static string? FilePath(string path)
    {
        if (SchemePattern().IsMatch(path))
        {
            if (!path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsUnc || uri.Host.Length > 0 && !uri.IsLoopback)
            {
                return null;
            }
            return uri.LocalPath;
        }
        return path;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]+:")]
    private static partial Regex SchemePattern();

    /// <summary>A path (it has a separator) or a name with an extension that has a letter in it (<c>.md</c>, <c>.gitignore</c>).</summary>
    private static bool LooksLikeFile(string path)
    {
        if (path.Contains('/', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal))
        {
            return path.Trim('/', '\\', '.', '~').Length > 0;
        }
        var dot = path.LastIndexOf('.');
        return dot >= 0 && dot < path.Length - 1 && !path.Contains(' ', StringComparison.Ordinal)
            && path[(dot + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && path[(dot + 1)..].Any(char.IsAsciiLetter);
    }
}
