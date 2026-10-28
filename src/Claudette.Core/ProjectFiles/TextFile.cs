using System.Text;
using Claudette.Core.Diffs;
using Claudette.Core.Files;

namespace Claudette.Core.ProjectFiles;

/// <summary>Why a file doesn't open in the editor.</summary>
public enum TextFileProblem
{
    None,

    /// <summary>It isn't there.</summary>
    Missing,

    /// <summary>It's binary: an image, a build product.</summary>
    Binary,

    /// <summary>It's over <see cref="TextFile.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>It couldn't be read: in use, or not permitted.</summary>
    Unreadable,
}

/// <summary>
/// A file read for the editor (DESIGN.md §18, "Project files", "Which files open"): its text, how it's encoded, and
/// whether it opens read-only and why.
/// </summary>
/// <param name="Text">The text, without a byte order mark. Empty when <paramref name="Problem"/> isn't None.</param>
/// <param name="Encoding">How it's encoded; null when it isn't valid text in an encoding Claudette writes back.</param>
/// <param name="ReadOnlyReason">Why it can't be edited, or null when it can.</param>
/// <param name="WriteProtected">
/// It's read-only only because the file is: what Perforce leaves a file it hasn't checked out as, which checking it out
/// changes (DESIGN.md §18, "Perforce in the Files tab").
/// </param>
public sealed record TextFileContent(string Path, string Text, TextFileEncoding? Encoding, string? ReadOnlyReason, TextFileProblem Problem = TextFileProblem.None,
    bool WriteProtected = false)
{
    public bool IsReadOnly => ReadOnlyReason is not null;

    /// <summary>How the chip writes the encoding: <c>UTF-8</c>, <c>UTF-8 with BOM</c>, <c>UTF-16 LE</c>…</summary>
    public string EncodingLabel => TextFile.EncodingLabel(Encoding);
}

/// <summary>
/// Reading a file for the editor, and saving it back as it was (DESIGN.md §18, "Project files", "Saving"): in its
/// encoding and byte order mark, through a temporary name, a link written through, its permissions kept, and only if
/// the file is still what was read.
/// </summary>
public static class TextFile
{
    /// <summary>Files bigger than this don't open: the editor says so, with <b>Open in the OS's app</b>.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>A file with a line longer than this opens read-only: typing on it takes about half a second a key.</summary>
    public const int LongestEditableLine = 1_000_000;

    /// <summary>What a file that's read-only on disk says.</summary>
    public const string WriteProtectedReason = "Claudette isn't allowed to write this file, so it's shown read-only.";

    /// <summary>Whether the file can be written now: it was read-only, and may have been checked out since.</summary>
    public static bool IsWritable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && CanWrite(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static TextFileContent Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Problem(path, TextFileProblem.Missing);
            }
            if (info.Length > MaxBytes)
            {
                return Problem(path, TextFileProblem.TooLarge);
            }
            var bytes = File.ReadAllBytes(path);
            if (!TextFileEncoding.TryRead(bytes, out var encoding, out var text))
            {
                // Not UTF-8, and no mark naming another encoding: binary, or text in an encoding nobody said.
                if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0)
                {
                    return Problem(path, TextFileProblem.Binary);
                }
                return new TextFileContent(path, Encoding.Latin1.GetString(bytes), null,
                    "This file isn't UTF-8 or UTF-16 text, so it's shown read-only: saving a guess at its encoding would change characters nobody touched.");
            }
            if (LineDiff.LooksBinary(text))
            {
                return Problem(path, TextFileProblem.Binary);
            }
            string? readOnly = null;
            var writeProtected = false;
            if (LongestLine(text) > LongestEditableLine)
            {
                readOnly = "This file has a line over a million characters, so it's shown read-only: typing on a line that long takes about half a second a key.";
            }
            else if (!CanWrite(info))
            {
                readOnly = WriteProtectedReason;
                writeProtected = true;
            }
            return new TextFileContent(path, text, encoding, readOnly, WriteProtected: writeProtected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Problem(path, TextFileProblem.Unreadable);
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/>, only if it still holds <paramref name="expectedOnDisk"/>
    /// (null: missing). False when it changed meanwhile, so nothing was written.
    /// </summary>
    /// <exception cref="IOException">It can't be written, or it isn't text that can be written back as it was.</exception>
    public static bool Save(string path, string? expectedOnDisk, string text) => Revert.Write(path, expectedOnDisk, text);

    /// <summary>The file's text now, for comparing with what was read: null when it's gone or can't be read as text.</summary>
    public static string? ReadTextOrNull(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var bytes = File.ReadAllBytes(path);
            return TextFileEncoding.TryRead(bytes, out _, out var text) ? text : Encoding.Latin1.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string EncodingLabel(TextFileEncoding? encoding)
    {
        if (encoding is null)
        {
            return "Not UTF-8";
        }
        var name = encoding.Encoding switch
        {
            UTF32Encoding utf32 => utf32.GetPreamble() is [0, 0, ..] ? "UTF-32 BE" : "UTF-32 LE",
            UnicodeEncoding utf16 => utf16.GetPreamble() is [0xFE, 0xFF] ? "UTF-16 BE" : "UTF-16 LE",
            _ => "UTF-8",
        };
        return name == "UTF-8" && encoding.HasMark ? "UTF-8 with BOM" : name;
    }

    /// <summary>The longest line's length, in characters.</summary>
    public static int LongestLine(string text)
    {
        var longest = 0;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] is '\n' or '\r')
            {
                longest = Math.Max(longest, i - start);
                start = i + 1;
            }
        }
        return longest;
    }

    internal static bool CanWrite(FileInfo info)
    {
        if (info.IsReadOnly)
        {
            return false;
        }
        if (OperatingSystem.IsWindows())
        {
            return true;
        }
        // The owner's write bit: close enough for a note, since writing will say if it's refused.
        return (info.UnixFileMode & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
    }

    private static TextFileContent Problem(string path, TextFileProblem problem) => new(path, "", null, null, problem);
}
