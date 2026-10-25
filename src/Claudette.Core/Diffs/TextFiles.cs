using System.Text;

namespace Claudette.Core.Diffs;

internal enum TextFileKind
{
    Text,
    Missing,
    TooLarge,
    Binary,
    Unreadable,
}

internal readonly record struct TextFileRead(TextFileKind Kind, string? Text);

/// <summary>
/// Reads a file for diffing: text only, up to a size cap. A byte order mark stays in the text as a leading
/// <c>﻿</c>, as Claude Code's <c>originalFile</c> has it, so the two compare alike and a mark added or removed can
/// be told (DESIGN.md §8, "What the lines don't show").
/// </summary>
public static class TextFiles
{
    /// <summary>Files larger than this aren't diffed (DESIGN.md §8).</summary>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    internal static TextFileRead Read(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new TextFileRead(TextFileKind.Missing, null);
            }
            if (info.Length > maxBytes)
            {
                return new TextFileRead(TextFileKind.TooLarge, null);
            }
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > maxBytes)
            {
                return new TextFileRead(TextFileKind.TooLarge, null);
            }
            var text = Decode(bytes);
            return LineDiff.LooksBinary(text)
                ? new TextFileRead(TextFileKind.Binary, null)
                : new TextFileRead(TextFileKind.Text, text);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new TextFileRead(TextFileKind.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new TextFileRead(TextFileKind.Unreadable, null);
        }
    }

    /// <summary>
    /// The whole file as text, whatever its size, with its byte order mark as a leading <c>﻿</c>: for the diff view,
    /// which compares it with Claude Code's <c>originalFile</c>.
    /// </summary>
    /// <exception cref="IOException">It can't be read.</exception>
    public static string ReadAll(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>UTF-8, or what a byte order mark says, with the mark kept.</summary>
    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var mark = reader.CurrentEncoding.Preamble;
        return mark.Length > 0 && bytes.AsSpan().StartsWith(mark) ? "﻿" + text : text;
    }
}
