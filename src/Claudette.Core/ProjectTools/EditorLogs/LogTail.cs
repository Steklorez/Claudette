using System.Text;
using Claudette.Core.Files;

namespace Claudette.Core.ProjectTools.EditorLogs;

/// <summary>What one read of a log found.</summary>
/// <param name="Lines">Its new lines, each whole: a last line still being written waits for the next read.</param>
/// <param name="Fresh">
/// These lines start the log afresh rather than carry it on: the first read, or the file was replaced (it got shorter
/// than what had been read, or its first bytes changed), as an editor does when it starts again.
/// </param>
/// <param name="Skipped">Bytes at the start of the file the first read passed over, to keep to <see cref="LogTail.Window"/>.</param>
public sealed record LogTailRead(IReadOnlyList<string> Lines, bool Fresh, long Skipped)
{
    public static LogTailRead None { get; } = new([], false, 0);
}

/// <summary>
/// Follows a log another program writes (DESIGN.md §18, "Editor logs") without ever holding on to it: each read opens
/// it for reading only, sharing read, write and delete, reads what's new, and closes it. So between reads Claudette has
/// no handle on it at all, and during one the writer can still write it, rename it or delete it. A file that can't be
/// opened right now (it's missing, or the writer shares nothing) waits for the next read.
/// </summary>
public sealed class LogTail
{
    /// <summary>The first read of a file bigger than this starts this far from its end, at a line's start.</summary>
    public const long DefaultWindow = 4 * 1024 * 1024;

    /// <summary>How much of the file's start is remembered, to tell a new file from the old one grown.</summary>
    private const int HeadLength = 256;

    private readonly Action<string>? _beforeWindow;
    private byte[]? _head;
    private long _offset;

    /// <param name="beforeWindow">
    /// Called with each line the first read passes over to keep to the window, so what's counted of the log covers all of it.
    /// </param>
    public LogTail(string path, long window = DefaultWindow, Action<string>? beforeWindow = null)
    {
        Path = path;
        Window = window;
        _beforeWindow = beforeWindow;
    }

    public string Path { get; }

    public long Window { get; }

    /// <summary>Opens a log as every read does: for reading, sharing everything, so the writer is never in the way.</summary>
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);

    /// <summary>The first line of a log, without a byte order mark; null when it can't be read now.</summary>
    public static string? FirstLine(string path)
    {
        try
        {
            using var stream = Open(path);
            var buffer = new byte[Math.Min(512, stream.Length)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var text = buffer.AsSpan(0, read);
            if (text.StartsWith(Encoding.UTF8.Preamble))
            {
                text = text[3..];
            }
            var end = text.IndexOfAny((byte)'\r', (byte)'\n');
            return Encoding.UTF8.GetString(end >= 0 ? text[..end] : text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What's new since the last read. With <paramref name="final"/>, as after its writer has ended, a last line without
    /// its line break counts too.
    /// </summary>
    public LogTailRead Read(bool final = false)
    {
        try
        {
            using var stream = Open(Path);
            return Read(stream, final);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, or the writer shares nothing for now: the next read tries again.
            return LogTailRead.None;
        }
    }

    private LogTailRead Read(FileStream stream, bool final)
    {
        var length = stream.Length;
        var head = new byte[(int)Math.Min(HeadLength, length)];
        stream.ReadExactly(head);
        var fresh = _head is null;
        if (_head is { } known && (length < _offset || !head.AsSpan(0, Math.Min(head.Length, known.Length)).SequenceEqual(known.AsSpan(0, Math.Min(head.Length, known.Length)))))
        {
            // Another file in its place: the writer started again.
            fresh = true;
            _offset = 0;
        }
        if (_head is null || head.Length >= _head.Length || fresh)
        {
            _head = head;
        }
        long skipped = 0;
        if (fresh && _offset == 0 && length > Window)
        {
            skipped = SkipTo(stream, length - Window);
            _offset = skipped;
        }
        var lines = new List<string>();
        foreach (var line in Utf8Lines.Read(stream, _offset))
        {
            if (line.End < 0)
            {
                if (final)
                {
                    lines.Add(line.Text);
                    _offset = length;
                }
                break;
            }
            lines.Add(line.Text);
            _offset = line.End;
        }
        return lines.Count == 0 && !fresh ? LogTailRead.None : new LogTailRead(lines, fresh, skipped);
    }

    /// <summary>
    /// Where the first line that starts at or after <paramref name="from"/> starts. The lines before it go to
    /// <c>beforeWindow</c>, when there is one.
    /// </summary>
    private long SkipTo(FileStream stream, long from)
    {
        if (_beforeWindow is not { } counted)
        {
            // From the byte before, so a line that starts right at it isn't passed over.
            foreach (var line in Utf8Lines.Read(stream, from - 1))
            {
                return line.End < 0 ? from : line.End;
            }
            return from;
        }
        long start = 0;
        foreach (var line in Utf8Lines.Read(stream, 0))
        {
            if (start >= from || line.End < 0)
            {
                break;
            }
            counted(line.Text);
            start = line.End;
        }
        return start;
    }
}
