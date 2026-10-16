using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>A file's line endings, for the editor's chip and its new lines (DESIGN.md §18, "Project files", "The chip").</summary>
public sealed class LineEndingsTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("one line", 0, 0, 0)]
    [InlineData("a\nb\n", 0, 2, 0)]
    [InlineData("a\r\nb\r\n", 2, 0, 0)]
    [InlineData("a\rb\r", 0, 0, 2)]
    [InlineData("a\r\nb\nc\rd", 1, 1, 1)]
    // A lone CR before a CRLF is one of each, and a CR at the very end is a CR.
    [InlineData("a\r\r\nb\r", 1, 0, 2)]
    [InlineData("\n\r", 0, 1, 1)]
    public void Each_kind_of_line_end_is_counted(string text, int crlf, int lf, int cr) =>
        Assert.Equal((crlf, lf, cr), LineEndings.Count(text));

    [Fact]
    public void Form_feeds_and_unicode_line_separators_arent_line_ends()
    {
        // A form feed, the line and paragraph separators, and NEL.
        var text = $"a\fb{(char)0x2028}c{(char)0x2029}d{(char)0x85}e";

        Assert.Equal((0, 0, 0), LineEndings.Count(text));
        Assert.Equal(LineEndingKind.None, LineEndings.Detect(text));
        Assert.Equal(text, LineEndings.Convert(text, LineEndingKind.Crlf));
    }

    [Theory]
    [InlineData("", LineEndingKind.None)]
    [InlineData("no newline at all", LineEndingKind.None)]
    [InlineData("a\nb\nc", LineEndingKind.Lf)]
    [InlineData("a\r\nb\r\n", LineEndingKind.Crlf)]
    [InlineData("a\r\nb\n", LineEndingKind.Mixed)]
    [InlineData("a\nb\rc", LineEndingKind.Mixed)]
    // Only lone CRs: there's no kind for them, so the chip says Mixed and offers LF or CRLF.
    [InlineData("a\rb\r", LineEndingKind.Mixed)]
    public void Detect_names_one_kind_or_mixed(string text, LineEndingKind kind) =>
        Assert.Equal(kind, LineEndings.Detect(text));

    [Theory]
    [InlineData("a\r\nb\r\nc\n", "\r\n")]
    [InlineData("a\r\nb\nc\n", "\n")]
    // A tie goes to LF.
    [InlineData("a\r\nb\n", "\n")]
    [InlineData("a\nb\n", "\n")]
    public void A_new_line_takes_what_most_lines_end_with(string text, string newline) =>
        Assert.Equal(newline, LineEndings.NewLineFor(text));

    [Theory]
    [InlineData("")]
    [InlineData("one line")]
    [InlineData("old mac\rline ends\r")]
    public void A_file_without_LF_or_CRLF_gets_the_OSs_newline(string text) =>
        Assert.Equal(Environment.NewLine, LineEndings.NewLineFor(text));

    [Theory]
    [InlineData("", LineEndingKind.None)]
    [InlineData("a\rb\r", LineEndingKind.None)]
    [InlineData("a\nb\n", LineEndingKind.Lf)]
    [InlineData("a\r\nb\r\n", LineEndingKind.Crlf)]
    [InlineData("a\r\nb\r\nc\n", LineEndingKind.Crlf)]
    [InlineData("a\r\nb\nc\n", LineEndingKind.Lf)]
    [InlineData("a\r\nb\n", LineEndingKind.Lf)]
    public void The_majority_is_LF_or_CRLF_with_a_tie_going_to_LF(string text, LineEndingKind kind) =>
        Assert.Equal(kind, LineEndings.Majority(text));

    [Theory]
    [InlineData("a\r\nb\nc\rd", LineEndingKind.Lf, "a\nb\nc\nd")]
    [InlineData("a\r\nb\nc\rd", LineEndingKind.Crlf, "a\r\nb\r\nc\r\nd")]
    [InlineData("a\r\n", LineEndingKind.Crlf, "a\r\n")]
    [InlineData("a\r\r\n", LineEndingKind.Lf, "a\n\n")]
    [InlineData("\r", LineEndingKind.Crlf, "\r\n")]
    [InlineData("", LineEndingKind.Crlf, "")]
    [InlineData("no newline", LineEndingKind.Crlf, "no newline")]
    // Anything but CRLF is taken as LF.
    [InlineData("a\r\nb", LineEndingKind.Mixed, "a\nb")]
    public void Converting_makes_every_line_end_the_same(string text, LineEndingKind to, string converted)
    {
        Assert.Equal(converted, LineEndings.Convert(text, to));
        Assert.Equal(converted, LineEndings.Convert(converted, to));
    }

    [Theory]
    [InlineData(LineEndingKind.Crlf, "CRLF")]
    [InlineData(LineEndingKind.Lf, "LF")]
    [InlineData(LineEndingKind.Mixed, "Mixed")]
    [InlineData(LineEndingKind.None, "LF")]
    public void The_chip_writes_the_kind(LineEndingKind kind, string label) => Assert.Equal(label, LineEndings.Label(kind));
}
