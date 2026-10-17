using System.Text.RegularExpressions;
using Claudette.Core.Protocol;

namespace Claudette.Core.ProjectTools;

/// <summary>
/// A link in a line of a project run's log (DESIGN.md §18, "Project tools", "A run's log"): a web address, or a file or
/// folder's path, with the line and column a compiler names.
/// </summary>
/// <param name="Start">Where it starts in the line.</param>
/// <param name="Length">How many characters of the line it takes.</param>
/// <param name="Url">The <c>http</c> or <c>https</c> address, for a web link.</param>
/// <param name="File">
/// The path as written, for a file link. Whether there's a file there is <see cref="ReplyFileFinder"/>'s to find out: a
/// path is only a link once it's found.
/// </param>
public sealed record RunLogLink(int Start, int Length, Uri? Url, ReplyFileReference? File);

/// <summary>
/// Finds the links in a line of a run's log: web addresses, and paths the way build tools write them, as MSVC, Unreal
/// and Unity do (<c>Player.cpp(42)</c>, <c>Assets/Player.cs(12,5)</c>), as gcc, clang and most others do
/// (<c>src/main.rs:42:7</c>), as Python does (<c>File "tool.py", line 42</c>), in quotes, or on their own with a folder
/// in them (<c>Saved/Logs/Game.log</c>, <c>C:\Work\Game\Binaries</c>). A name on its own (<c>Player.cpp</c>) is a path
/// only with a line number after it.
/// </summary>
public static partial class RunLogLinks
{
    /// <summary>Only so much of a long line is looked through, so a line of minified output costs no more than this.</summary>
    public const int MaxLookedAt = 4000;

    private const int MaxPathLength = 400;

    /// <summary>The links in <paramref name="line"/>, in order, none overlapping.</summary>
    public static IReadOnlyList<RunLogLink> Find(string line)
    {
        if (line.Length == 0)
        {
            return [];
        }
        var text = line.Length > MaxLookedAt ? line[..MaxLookedAt] : line;
        var links = new List<RunLogLink>();
        foreach (Match match in WebPattern().Matches(text))
        {
            var address = TrimAddress(match.Value);
            if (Uri.TryCreate(address, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            {
                links.Add(new RunLogLink(match.Index, address.Length, url, null));
            }
        }
        // The more a pattern says, the sooner it's tried: a path with its line number beats the same path on its own.
        AddPaths(links, text, PythonPattern());
        AddPaths(links, text, DotNetPattern());
        AddPaths(links, text, QuotedPattern());
        AddPaths(links, text, DriveWithParenthesesPattern(), withLine: true);
        AddPaths(links, text, DriveWithColonPattern(), withLine: true);
        AddPaths(links, text, WithParenthesesPattern(), withLine: true);
        AddPaths(links, text, WithColonPattern(), withLine: true);
        AddPaths(links, text, PlainPattern(), needsFolder: true);
        links.Sort((a, b) => a.Start.CompareTo(b.Start));
        return links;
    }

    /// <summary>
    /// Each match of <paramref name="pattern"/> that's a path and doesn't overlap a link already found. Its <c>path</c>
    /// group is the link, and with <paramref name="withLine"/> the line number after it too (<c>Player.cpp(42,7)</c>);
    /// <c>line</c> and <c>column</c> are where in the file it points.
    /// </summary>
    private static void AddPaths(List<RunLogLink> links, string text, Regex pattern, bool needsFolder = false, bool withLine = false)
    {
        foreach (Match match in pattern.Matches(text))
        {
            var group = match.Groups["path"];
            var path = needsFolder ? TrimPlain(group.Value) : group.Value;
            if (!IsPath(path, needsFolder) || links.Any(l => l.Start < match.Index + match.Length && match.Index < l.Start + l.Length))
            {
                continue;
            }
            var line = Number(match.Groups["line"]);
            var length = withLine ? match.Index + match.Length - group.Index : path.Length;
            links.Add(new RunLogLink(group.Index, length, null,
                new ReplyFileReference(text.Substring(group.Index, length), path, line, line is null ? null : Number(match.Groups["column"]))));
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> can be a file's path: not too long, with a letter in it (<c>12.5</c> and
    /// <c>3/120</c> aren't), and not on the network, which Windows would reach just by looking. With
    /// <paramref name="needsFolder"/>, it has a folder in it too, so a word with a dot isn't looked for.
    /// </summary>
    private static bool IsPath(string path, bool needsFolder)
    {
        if (path.Length is 0 or > MaxPathLength || !path.Any(char.IsLetter) || path.Any(char.IsControl)
            || path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/')
        {
            return false;
        }
        if (!needsFolder)
        {
            return true;
        }
        var separator = path.IndexOfAny(['/', '\\']);
        // "/" or "~/" alone isn't a path worth a link; a folder in it, or a rooted one with a name, is.
        return separator >= 0 && path.TrimEnd('/', '\\').Length > (path[0] == '~' ? 2 : 1);
    }

    private static int? Number(Group group) => group.Success && int.TryParse(group.ValueSpan, out var value) && value > 0 ? value : null;

    /// <summary>A web address without what ends the sentence it's in: <c>(see https://example.com/a).</c> is <c>…/a</c>.</summary>
    private static string TrimAddress(string address)
    {
        while (address.Length > 0)
        {
            var last = address[^1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"'
                || last == ')' && address.Count(c => c == ')') > address.Count(c => c == '(')
                || last == ']' && address.Count(c => c == ']') > address.Count(c => c == '[')
                || last == '}' && address.Count(c => c == '}') > address.Count(c => c == '{'))
            {
                address = address[..^1];
                continue;
            }
            break;
        }
        return address;
    }

    /// <summary>A path on its own without the end of the sentence it's in: <c>Saved/Logs/Game.log.</c> is <c>…/Game.log</c>.</summary>
    private static string TrimPlain(string path) => path.TrimEnd('.', ',', ';', '!', '?');

    [GeneratedRegex(@"\bhttps?://[^\s<>""'`]+", RegexOptions.IgnoreCase)]
    private static partial Regex WebPattern();

    /// <summary>Python's tracebacks: <c>File "tools/build.py", line 42</c>.</summary>
    [GeneratedRegex(@"File ""(?<path>[^""<>|?*\r\n]+)"", line (?<line>\d{1,7})")]
    private static partial Regex PythonPattern();

    /// <summary>.NET's stack traces: <c>at Game.Tools.Pack() in C:\Work\Tools\Pack.cs:line 42</c>.</summary>
    [GeneratedRegex(@"\bin (?<path>(?:[A-Za-z]:[\\/]|/)[^""<>|?*\r\n:]*?):line (?<line>\d{1,7})")]
    private static partial Regex DotNetPattern();

    /// <summary>
    /// A path in quotes, which may have spaces: <c>"C:\Program Files\Epic Games\UE_5.4"</c>. A quote inside a word
    /// (<c>can't</c>) isn't one.
    /// </summary>
    [GeneratedRegex(@"(?<![\w])(?<q>[""'])(?<path>[^""'<>|?*\r\n]*[\\/][^""'<>|?*\r\n]*)\k<q>(?![\w])")]
    private static partial Regex QuotedPattern();

    /// <summary>
    /// MSVC's, Unreal's and Unity's way with a drive, which may have spaces and parentheses:
    /// <c>C:\Program Files (x86)\…\xstring(123,5): warning</c>.
    /// </summary>
    [GeneratedRegex(@"(?<![\w])(?<path>[A-Za-z]:[\\/][^""<>|?*\r\n:]*?)\((?<line>\d{1,7})(?:,(?<column>\d{1,7}))?(?:[,\-]\d{1,7})*\)")]
    private static partial Regex DriveWithParenthesesPattern();

    /// <summary>gcc's and clang's way with a drive, which may have spaces: <c>C:\My Game\Source\Player.cpp:42:7: error</c>.</summary>
    [GeneratedRegex(@"(?<![\w])(?<path>[A-Za-z]:[\\/][^""<>|?*\r\n:]*?):(?<line>\d{1,7})(?::(?<column>\d{1,7}))?")]
    private static partial Regex DriveWithColonPattern();

    /// <summary>MSVC's and Unity's way without a drive: <c>Assets/Scripts/Player.cs(12,5): error CS0103</c>, <c>Player.cpp(42)</c>.</summary>
    [GeneratedRegex(@"(?<![^\s""'(\[<>=,])(?<path>[^\s""'<>|?*:()\[\]]*[./\\][^\s""'<>|?*:()\[\]]*)\((?<line>\d{1,7})(?:,(?<column>\d{1,7}))?(?:[,\-]\d{1,7})*\)")]
    private static partial Regex WithParenthesesPattern();

    /// <summary>gcc's, clang's, rustc's and most others' way: <c>src/main.rs:42:7</c>, <c>/usr/include/stdio.h:33</c>.</summary>
    [GeneratedRegex(@"(?<![^\s""'(\[<>=,])(?<path>[^\s""'<>|?*:()\[\]]*[./\\][^\s""'<>|?*:()\[\]]*):(?<line>\d{1,7})(?::(?<column>\d{1,7}))?")]
    private static partial Regex WithColonPattern();

    /// <summary>A path on its own, with a folder in it: <c>Saved/Logs/Game.log</c>, <c>C:\Work\Game\Binaries</c>, <c>~/builds</c>.</summary>
    [GeneratedRegex(@"(?<![^\s""'(\[<>=,])(?<path>(?:[A-Za-z]:|~)?[\\/][^\s""'<>|?*:()\[\]]*|[^\s""'<>|?*:()\[\]]+[\\/][^\s""'<>|?*:()\[\]]*)")]
    private static partial Regex PlainPattern();
}
