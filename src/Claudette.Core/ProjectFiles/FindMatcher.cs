using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Claudette.Core.ProjectFiles;

/// <summary>What Find in files looks for (DESIGN.md §18, "Project files", "Finding in files").</summary>
/// <param name="Text">What's typed: text, or with <paramref name="IsRegex"/> a .NET regular expression.</param>
/// <param name="MatchCase">Upper and lower case count; otherwise they're the same.</param>
/// <param name="WholeWords">A match starts and ends at a word's edge: not inside a word.</param>
public sealed record FindQuery(string Text, bool MatchCase = false, bool WholeWords = false, bool IsRegex = false);

/// <summary>
/// Finds a <see cref="FindQuery"/> in a line (DESIGN.md §18, "Project files", "Finding in files"). Matching is line by
/// line, so a regular expression never spans lines, and gives up on a line after <see cref="RegexTimeout"/>. A match
/// is never empty: a pattern that can match nothing (<c>^</c>, <c>a*</c>) finds only its non-empty matches.
/// </summary>
public sealed class FindMatcher
{
    /// <summary>How long a regular expression may take on one line before the file is skipped.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex? _regex;
    private readonly StringComparison _comparison;

    private FindMatcher(FindQuery query, Regex? regex)
    {
        Query = query;
        _regex = regex;
        _comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    public FindQuery Query { get; }

    /// <summary>
    /// The matcher for <paramref name="query"/>; false when there's nothing to look for (<paramref name="problem"/> null)
    /// or it can't be looked for (<paramref name="problem"/> says why: a regular expression that isn't valid, a line break).
    /// </summary>
    public static bool TryCreate(FindQuery query, [NotNullWhen(true)] out FindMatcher? matcher, out string? problem)
    {
        matcher = null;
        problem = null;
        if (query.Text.Length == 0)
        {
            return false;
        }
        if (!query.IsRegex && query.Text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            problem = "Find in files looks within lines, so what's found can't hold a line break.";
            return false;
        }
        Regex? regex = null;
        if (query.IsRegex)
        {
            try
            {
                var options = RegexOptions.CultureInvariant | RegexOptions.Compiled | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                regex = new Regex(query.Text, options, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                problem = $"That isn't a regular expression .NET can read: {ex.Message}";
                return false;
            }
        }
        matcher = new FindMatcher(query, regex);
        return true;
    }

    /// <summary>The matcher for <paramref name="query"/>, which must be one there's something to look for in.</summary>
    /// <exception cref="ArgumentException">There's nothing to look for, or it can't be looked for.</exception>
    public static FindMatcher Create(FindQuery query) =>
        TryCreate(query, out var matcher, out var problem) ? matcher : throw new ArgumentException(problem ?? "There's nothing to find.", nameof(query));

    /// <summary>Whether the line may hold a match: a quick look before the line is taken apart.</summary>
    /// <exception cref="RegexMatchTimeoutException">The regular expression took too long on it.</exception>
    public bool MayMatch(ReadOnlySpan<char> line) =>
        _regex is { } regex ? regex.IsMatch(line) : line.IndexOf(Query.Text, _comparison) >= 0;

    /// <summary>Adds each match in <paramref name="line"/> to <paramref name="matches"/>: left to right, none overlapping, none empty.</summary>
    /// <exception cref="RegexMatchTimeoutException">The regular expression took too long on it.</exception>
    public void Find(string line, List<(int Start, int Length)> matches)
    {
        if (_regex is { } regex)
        {
            var at = 0;
            while (at <= line.Length)
            {
                var match = regex.Match(line, at);
                if (!match.Success)
                {
                    break;
                }
                if (match.Length > 0 && (!Query.WholeWords || IsWholeWord(line, match.Index, match.Length)))
                {
                    matches.Add((match.Index, match.Length));
                    at = match.Index + match.Length;
                }
                else
                {
                    at = match.Index + 1;
                }
            }
            return;
        }
        var text = Query.Text;
        for (var at = line.IndexOf(text, _comparison); at >= 0; at = line.IndexOf(text, at, _comparison))
        {
            if (!Query.WholeWords || IsWholeWord(line, at, text.Length))
            {
                matches.Add((at, text.Length));
                at += text.Length;
            }
            else
            {
                at++;
            }
            if (at >= line.Length)
            {
                break;
            }
        }
    }

    /// <summary>
    /// What the match at <paramref name="start"/> in <paramref name="line"/> becomes: the replacement as typed, or for
    /// a regular expression with its groups put in (<c>$1</c>, <c>${name}</c>, <c>$0</c>; <c>$$</c> for a dollar sign).
    /// </summary>
    public string Replacement(string line, int start, int length, string replacement)
    {
        if (_regex is not { } regex)
        {
            return replacement;
        }
        var match = regex.Match(line, start);
        return match.Success && match.Index == start && match.Length == length ? match.Result(replacement) : replacement;
    }

    /// <summary>A word's characters: letters, digits and the underscore.</summary>
    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// The match starts and ends at a word's edge: what's before it and its first character aren't both a word's, nor
    /// are its last character and what's after it. So <c>Dash</c> is found in <c>Dash()</c> but not in <c>DashCost</c>.
    /// </summary>
    internal static bool IsWholeWord(string line, int start, int length)
    {
        var end = start + length;
        var startsWord = start == 0 || !IsWordCharacter(line[start - 1]) || !IsWordCharacter(line[start]);
        var endsWord = end >= line.Length || !IsWordCharacter(line[end]) || !IsWordCharacter(line[end - 1]);
        return startsWord && endsWord;
    }
}

/// <summary>
/// Find in files' <b>File mask</b> (DESIGN.md §18, "Project files", "Finding in files"): the files to look in, by name,
/// as <c>*.cs, *.h</c>, with <c>!</c> before those to leave out (<c>!*.Designer.cs</c>). Patterns are separated by
/// commas, semicolons or spaces; <c>*</c> is any run of characters and <c>?</c> any one, ignoring case. A pattern with a
/// slash matches the path from the project's folder instead, where <c>*</c> stays within a folder and <c>**</c> goes
/// through them (<c>Source/**/*.cpp</c>). <c>.cs</c> is read as <c>*.cs</c>. No pattern: every file.
/// </summary>
public sealed class FileMask
{
    private readonly IReadOnlyList<(Regex Regex, bool OnPath)> _include;
    private readonly IReadOnlyList<(Regex Regex, bool OnPath)> _exclude;

    private FileMask(string text, IReadOnlyList<(Regex, bool)> include, IReadOnlyList<(Regex, bool)> exclude)
    {
        Text = text;
        _include = include;
        _exclude = exclude;
    }

    /// <summary>Every file.</summary>
    public static FileMask All { get; } = new("", [], []);

    /// <summary>The mask as typed.</summary>
    public string Text { get; }

    public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

    public static FileMask Parse(string? text)
    {
        var include = new List<(Regex, bool)>();
        var exclude = new List<(Regex, bool)>();
        foreach (var part in (text ?? "").Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var leaveOut = part.StartsWith('!');
            var pattern = (leaveOut ? part[1..] : part).Replace('\\', '/').Trim();
            if (pattern.Length == 0)
            {
                continue;
            }
            if (pattern[0] == '.' && pattern.AsSpan().IndexOfAny('*', '?') < 0)
            {
                pattern = "*" + pattern;
            }
            (leaveOut ? exclude : include).Add((ToRegex(pattern), pattern.Contains('/')));
        }
        return include.Count == 0 && exclude.Count == 0 ? All : new FileMask(text ?? "", include, exclude);
    }

    /// <summary>Whether the file at <paramref name="relativePath"/> (from the project's folder, with forward slashes) is looked in.</summary>
    public bool Matches(string relativePath)
    {
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        bool Hits((Regex Regex, bool OnPath) pattern) => pattern.Regex.IsMatch(pattern.OnPath ? relativePath : name);
        return (_include.Count == 0 || _include.Any(Hits)) && !_exclude.Any(Hits);
    }

    private static Regex ToRegex(string glob)
    {
        var pattern = new System.Text.StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                pattern.Append(".*");
                i++;
                // "**/" also matches no folder at all.
                if (i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    pattern.Append("/?");
                    i++;
                }
            }
            else if (c == '*')
            {
                pattern.Append("[^/]*");
            }
            else if (c == '?')
            {
                pattern.Append("[^/]");
            }
            else
            {
                pattern.Append(Regex.Escape(c.ToString()));
            }
        }
        return new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
