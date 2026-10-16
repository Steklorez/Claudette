namespace Claudette.Core.ProjectFiles;

/// <summary>Which line endings a file has: the editor's chip shows them, and new lines take them.</summary>
public enum LineEndingKind
{
    /// <summary>No line ends at all: one line, or none.</summary>
    None,

    Lf,

    Crlf,

    /// <summary>More than one kind.</summary>
    Mixed,
}

/// <summary>
/// A file's line endings (DESIGN.md §18, "Project files", "The chip"): finding them, the one new lines take, and
/// changing every line's. Only <c>\r\n</c>, <c>\n</c> and a lone <c>\r</c> count, unlike
/// <see cref="string.ReplaceLineEndings()"/>, which would also change form feeds and Unicode line separators nobody
/// asked to change.
/// </summary>
public static class LineEndings
{
    /// <summary>How many lines end in each way.</summary>
    public static (int Crlf, int Lf, int Cr) Count(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    crlf++;
                    i++;
                    break;
                case '\r':
                    cr++;
                    break;
                case '\n':
                    lf++;
                    break;
            }
        }
        return (crlf, lf, cr);
    }

    public static LineEndingKind Detect(string text)
    {
        var (crlf, lf, cr) = Count(text);
        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        return kinds switch
        {
            0 => LineEndingKind.None,
            > 1 => LineEndingKind.Mixed,
            _ => crlf > 0 ? LineEndingKind.Crlf : lf > 0 ? LineEndingKind.Lf : LineEndingKind.Mixed,
        };
    }

    /// <summary>
    /// The newline a new line in <paramref name="text"/> takes: what most of its lines end with, or the OS's own for a
    /// file with none.
    /// </summary>
    public static string NewLineFor(string text)
    {
        var (crlf, lf, _) = Count(text);
        return crlf == 0 && lf == 0 ? Environment.NewLine : crlf > lf ? "\r\n" : "\n";
    }

    /// <summary>Which of LF and CRLF most lines end with; <see cref="LineEndingKind.None"/> for a file with neither.</summary>
    public static LineEndingKind Majority(string text)
    {
        var (crlf, lf, _) = Count(text);
        return crlf == 0 && lf == 0 ? LineEndingKind.None : crlf > lf ? LineEndingKind.Crlf : LineEndingKind.Lf;
    }

    /// <summary><paramref name="text"/> with every line ending made <paramref name="to"/>'s: LF or CRLF.</summary>
    public static string Convert(string text, LineEndingKind to)
    {
        var newline = to == LineEndingKind.Crlf ? "\r\n" : "\n";
        var builder = new System.Text.StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                builder.Append(newline);
            }
            else if (c == '\n')
            {
                builder.Append(newline);
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    /// <summary>How the chip writes it.</summary>
    public static string Label(LineEndingKind kind) => kind switch
    {
        LineEndingKind.Crlf => "CRLF",
        LineEndingKind.Mixed => "Mixed",
        _ => "LF",
    };
}
