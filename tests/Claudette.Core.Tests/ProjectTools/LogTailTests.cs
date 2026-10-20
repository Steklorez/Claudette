using System.Text;
using Claudette.Core.ProjectTools.EditorLogs;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectTools;

/// <summary>
/// Following a log another program writes (DESIGN.md §18, "Editor logs"): what's new at each read, a file replaced as an
/// editor starting again replaces it, a big file's end, and never a handle on the file between reads.
/// </summary>
public sealed class LogTailTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-logtail");

    public void Dispose() => _temp.Dispose();

    private string LogPath => _temp.Combine("Saved", "Logs", "NightOwl.log");

    private void Write(string text) => Append(text, create: true);

    private void Append(string text, bool create = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        using var stream = new FileStream(LogPath, create ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void Each_read_brings_the_lines_written_since_and_a_line_being_written_waits()
    {
        Write("Log file open, 10/20/26 15:01:22\nLogInit: Display: Starting\nLogInit: Disp");
        var tail = new LogTail(LogPath);

        var first = tail.Read();
        Assert.True(first.Fresh);
        Assert.Equal(["Log file open, 10/20/26 15:01:22", "LogInit: Display: Starting"], first.Lines);
        Assert.Equal(0, first.Skipped);

        Append("lay: Engine ready\r\nLogTemp: Warning: Slow\n");
        var second = tail.Read();
        Assert.False(second.Fresh);
        Assert.Equal(["LogInit: Display: Engine ready", "LogTemp: Warning: Slow"], second.Lines);

        Assert.Same(LogTailRead.None, tail.Read());
    }

    [Fact]
    public void A_last_line_without_its_break_counts_once_its_writer_has_ended()
    {
        Write("one\ntwo");
        var tail = new LogTail(LogPath);
        Assert.Equal(["one"], tail.Read().Lines);

        Assert.Equal(["two"], tail.Read(final: true).Lines);
        Assert.Empty(tail.Read(final: true).Lines);
    }

    [Fact]
    public void A_byte_order_mark_is_skipped_and_a_character_split_between_writes_comes_whole()
    {
        var tail = new LogTail(LogPath);
        var bytes = Encoding.UTF8.GetBytes("Café ☕ ready\n");
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllBytes(LogPath, [.. Encoding.UTF8.Preamble, .. bytes[..4]]);
        Assert.Empty(tail.Read().Lines);

        using (var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.Write(bytes.AsSpan(4));
        }

        Assert.Equal(["Café ☕ ready"], tail.Read().Lines);
        Assert.Equal("Café ☕ ready", LogTail.FirstLine(LogPath));
    }

    [Fact]
    public void A_missing_file_waits_and_its_first_read_once_it_is_there_starts_the_log()
    {
        var tail = new LogTail(LogPath);
        Assert.Same(LogTailRead.None, tail.Read());

        Write("first\n");

        var read = tail.Read();
        Assert.True(read.Fresh);
        Assert.Equal(["first"], read.Lines);
    }

    [Fact]
    public void A_new_file_in_the_old_ones_place_starts_the_log_afresh_whatever_its_length()
    {
        Write("Log file open, 10/20/26 15:01:22\nold line one\nold line two\n");
        var tail = new LogTail(LogPath);
        tail.Read();

        // As Unreal starts again: the old log becomes a backup, and a new one begins, longer than the old already.
        File.Move(LogPath, _temp.Combine("Saved", "Logs", "NightOwl-backup-2026.10.20-15.30.00.log"));
        Write("Log file open, 10/20/26 15:30:05\nnew line one, which is quite a bit longer than the old ones were\n");

        var read = tail.Read();
        Assert.True(read.Fresh);
        Assert.Equal(["Log file open, 10/20/26 15:30:05", "new line one, which is quite a bit longer than the old ones were"], read.Lines);
    }

    [Fact]
    public void A_file_that_got_shorter_starts_the_log_afresh()
    {
        Write("same start\nand a long second line\n");
        var tail = new LogTail(LogPath);
        tail.Read();

        Write("same start\n");

        var read = tail.Read();
        Assert.True(read.Fresh);
        Assert.Equal(["same start"], read.Lines);
    }

    [Fact]
    public void A_big_file_is_read_from_a_line_near_its_end_and_the_lines_before_are_counted()
    {
        var lines = Enumerable.Range(1, 1000).Select(i => $"line {i:0000}").ToArray();
        Write(string.Join("\n", lines) + "\n");
        var before = new List<string>();
        // Each line is 10 bytes with its break: a window of 95 bytes starts at the first whole line in it.
        var tail = new LogTail(LogPath, window: 95, beforeWindow: before.Add);

        var read = tail.Read();

        Assert.True(read.Fresh);
        Assert.Equal(lines[^9..], read.Lines);
        Assert.Equal(lines[..^9], before);
        Assert.Equal(9910, read.Skipped);

        // Without a counter, the same lines.
        var plain = new LogTail(LogPath, window: 95).Read();
        Assert.Equal(lines[^9..], plain.Lines);
    }

    [Fact]
    public void A_writer_that_shares_only_reading_is_read_and_can_still_write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        using var writer = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(Encoding.UTF8.GetBytes("held\n"));
        writer.Flush();
        var tail = new LogTail(LogPath);

        Assert.Equal(["held"], tail.Read().Lines);

        writer.Write(Encoding.UTF8.GetBytes("still writing\n"));
        writer.Flush();
        Assert.Equal(["still writing"], tail.Read().Lines);
    }

    [Fact]
    public void Between_reads_nothing_holds_the_file_so_it_can_be_renamed_and_deleted()
    {
        Write("one\n");
        var tail = new LogTail(LogPath);
        tail.Read();
        LogTail.FirstLine(LogPath);

        var moved = _temp.Combine("Saved", "Logs", "NightOwl-backup.log");
        File.Move(LogPath, moved);
        File.Delete(moved);

        Assert.False(File.Exists(moved));
        Assert.Same(LogTailRead.None, tail.Read());
    }
}
