using System.Text;
using Claudette.Core.ProjectFiles;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>Reading files for the editor and saving them back as they were (DESIGN.md §18, "Project files", "Which files open", "Saving").</summary>
public sealed class TextFileTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-textfile");

    public void Dispose() => _temp.Dispose();

    public static TheoryData<byte[], string, string> Encodings => new()
    {
        { [(byte)'h', (byte)'i', (byte)'\n'], "hi\n", "UTF-8" },
        { [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'], "hi", "UTF-8 with BOM" },
        { [0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0], "hi", "UTF-16 LE" },
        { [0xFE, 0xFF, 0, (byte)'h', 0, (byte)'i'], "hi", "UTF-16 BE" },
        { [0xFF, 0xFE, 0, 0, (byte)'h', 0, 0, 0], "h", "UTF-32 LE" },
        { [0, 0, 0xFE, 0xFF, 0, 0, 0, (byte)'h'], "h", "UTF-32 BE" },
        { [0xC3, 0xA9], "é", "UTF-8" },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Text_opens_in_its_encoding_without_its_mark(byte[] bytes, string text, string label)
    {
        var path = WriteBytes("file.txt", bytes);

        var content = TextFile.Read(path);

        Assert.Equal(TextFileProblem.None, content.Problem);
        Assert.Equal(text, content.Text);
        Assert.Equal(label, content.EncodingLabel);
        Assert.False(content.IsReadOnly);
        Assert.Equal(path, content.Path);
    }

    [Fact]
    public void An_empty_file_opens_as_utf8()
    {
        var content = TextFile.Read(WriteBytes("empty.txt", []));

        Assert.Equal((TextFileProblem.None, "", "UTF-8", false), (content.Problem, content.Text, content.EncodingLabel, content.IsReadOnly));
    }

    [Fact]
    public void A_missing_file_doesnt_open()
    {
        var content = TextFile.Read(_temp.Combine("missing.txt"));

        Assert.Equal(TextFileProblem.Missing, content.Problem);
        Assert.Equal("", content.Text);
        Assert.Null(content.Encoding);
        Assert.False(content.IsReadOnly);
    }

    [Fact]
    public void A_deleted_file_reads_as_missing()
    {
        var path = _temp.Write("gone.txt", "text");
        Assert.Equal(TextFileProblem.None, TextFile.Read(path).Problem);

        File.Delete(path);

        Assert.Equal(TextFileProblem.Missing, TextFile.Read(path).Problem);
        Assert.Null(TextFile.ReadTextOrNull(path));
    }

    [Fact]
    public void A_binary_file_doesnt_open()
    {
        // Not UTF-8, with NUL bytes: a PNG's header.
        Assert.Equal(TextFileProblem.Binary, TextFile.Read(WriteBytes("image.png", [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D])).Problem);
        // Valid UTF-8, but with a NUL in it.
        Assert.Equal(TextFileProblem.Binary, TextFile.Read(WriteBytes("data.bin", [(byte)'a', 0, (byte)'b'])).Problem);
    }

    [Fact]
    public void A_file_over_the_size_cap_doesnt_open()
    {
        var path = _temp.Combine("huge.log");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            // Sparse, or at least unwritten: nothing is read past the length.
            stream.SetLength(TextFile.MaxBytes + 1);
        }

        var content = TextFile.Read(path);

        Assert.Equal(TextFileProblem.TooLarge, content.Problem);
        Assert.Equal("", content.Text);
    }

    [Fact]
    public void A_file_held_open_by_another_program_is_unreadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows lets a program keep others from reading a file.");
        var path = _temp.Write("locked.txt", "text");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(TextFileProblem.Unreadable, TextFile.Read(path).Problem);
        Assert.Null(TextFile.ReadTextOrNull(path));
    }

    [Fact]
    public void Text_that_isnt_utf8_opens_read_only_as_latin1()
    {
        // "café" in Latin-1: no NUL, so it's text, but in an encoding nobody said.
        var path = WriteBytes("latin1.txt", [(byte)'c', (byte)'a', (byte)'f', 0xE9]);

        var content = TextFile.Read(path);

        Assert.Equal(TextFileProblem.None, content.Problem);
        Assert.Equal("café", content.Text);
        Assert.Null(content.Encoding);
        Assert.Equal("Not UTF-8", content.EncodingLabel);
        Assert.True(content.IsReadOnly);
        Assert.Contains("UTF-8", content.ReadOnlyReason, StringComparison.Ordinal);
        // Compared later with what's on disk, it reads the same.
        Assert.Equal(content.Text, TextFile.ReadTextOrNull(path));
    }

    [Fact]
    public void A_file_with_a_line_over_a_million_characters_opens_read_only()
    {
        var editable = TextFile.Read(_temp.Write("edge.min.js", new string('a', TextFile.LongestEditableLine) + "\nb"));
        var tooLong = TextFile.Read(_temp.Write("long.min.js", "a\n" + new string('a', TextFile.LongestEditableLine + 1)));

        Assert.False(editable.IsReadOnly);
        Assert.True(tooLong.IsReadOnly);
        Assert.Contains("million", tooLong.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Equal(TextFileProblem.None, tooLong.Problem);
    }

    [Fact]
    public void A_file_that_cant_be_written_opens_read_only()
    {
        var path = _temp.Write("readonly.txt", "text");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var content = TextFile.Read(path);

        Assert.Equal("text", content.Text);
        Assert.True(content.IsReadOnly);
        Assert.Contains("allowed", content.ReadOnlyReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abcd", 4)]
    [InlineData("ab\ncde\r\nf", 3)]
    [InlineData("abc\r\n", 3)]
    [InlineData("a\rbcdef\rgh", 5)]
    [InlineData("\n\n", 0)]
    public void The_longest_line_is_counted_between_any_line_ends(string text, int longest) =>
        Assert.Equal(longest, TextFile.LongestLine(text));

    [Fact]
    public void Saving_keeps_the_encoding_and_mark()
    {
        var mark = new byte[] { 0xFF, 0xFE };
        var path = WriteBytes("utf16.txt", [.. mark, .. Encoding.Unicode.GetBytes("a\r\nb")]);
        var read = TextFile.Read(path);

        Assert.True(TextFile.Save(path, read.Text, "a\r\nc"));

        Assert.Equal([.. mark, .. Encoding.Unicode.GetBytes("a\r\nc")], File.ReadAllBytes(path));
        Assert.Equal("a\r\nc", TextFile.ReadTextOrNull(path));
    }

    [Fact]
    public void Saving_keeps_a_utf8_mark()
    {
        var path = WriteBytes("bom.txt", [0xEF, 0xBB, 0xBF, (byte)'a']);

        Assert.True(TextFile.Save(path, TextFile.Read(path).Text, "b"));

        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'b'], File.ReadAllBytes(path));
    }

    [Fact]
    public void Saving_writes_nothing_over_a_file_that_changed_since_it_was_read()
    {
        var path = _temp.Write("changed.txt", "as read");
        var read = TextFile.Read(path);
        File.WriteAllText(path, "changed by Claude");

        Assert.False(TextFile.Save(path, read.Text, "my edit"));

        Assert.Equal("changed by Claude", File.ReadAllText(path));
    }

    [Fact]
    public void Saving_a_deleted_file_writes_it_back_as_utf8()
    {
        var path = _temp.Write("deleted.txt", "as read");
        File.Delete(path);

        // Deleted under the editor, whose text stays; Save writes it back, as missing is what's on disk.
        Assert.True(TextFile.Save(path, null, "é"));
        Assert.Equal([0xC3, 0xA9], File.ReadAllBytes(path));
    }

    [Fact]
    public void Saving_writes_nothing_over_a_file_that_appeared_since_it_was_found_missing()
    {
        var path = _temp.Combine("appeared.txt");
        Assert.Equal(TextFileProblem.Missing, TextFile.Read(path).Problem);
        File.WriteAllText(path, "made by Claude");

        Assert.False(TextFile.Save(path, null, "mine"));

        Assert.Equal("made by Claude", File.ReadAllText(path));
    }

    [Fact]
    public void Saving_over_text_that_isnt_utf8_throws()
    {
        var path = WriteBytes("latin1.txt", [(byte)'c', 0xE9]);

        Assert.Throws<IOException>(() => TextFile.Save(path, TextFile.Read(path).Text, "ce"));
        Assert.Equal([(byte)'c', 0xE9], File.ReadAllBytes(path));
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
