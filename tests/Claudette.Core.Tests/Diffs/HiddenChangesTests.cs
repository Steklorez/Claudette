using Claudette.Core.Diffs;
using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.Diffs;

/// <summary>What changed in a file that its lines don't show (DESIGN.md §8, "What the lines don't show").</summary>
public sealed class HiddenChangesTests
{
    [Fact]
    public void Line_endings_are_told_only_when_both_sides_are_known()
    {
        // Claude Code reports a CRLF file with \n, so the text alone can't say: the hook's read does.
        Assert.Equal(new LineEndingChange(LineEndingKind.Crlf, LineEndingKind.Lf),
            HiddenChanges.Between("a\nb\n", LineEndingKind.Crlf, "a\nb\n", LineEndingKind.Lf)?.LineEndings);
        Assert.Equal(new LineEndingChange(LineEndingKind.Mixed, LineEndingKind.Crlf),
            HiddenChanges.Between("a\r\nb\n", LineEndingKind.Mixed, "a\r\nb\r\n", LineEndingKind.Crlf)?.LineEndings);
        Assert.Null(HiddenChanges.Between("a\nb\n", null, "a\r\nb\r\n", LineEndingKind.Crlf));
        Assert.Null(HiddenChanges.Between("a\nb\n", LineEndingKind.Crlf, "a\r\nb\r\n", null));
        Assert.Null(HiddenChanges.Between("a\nb\n", LineEndingKind.Crlf, "a\r\nb\r\n", LineEndingKind.Crlf));
    }

    [Fact]
    public void A_file_with_one_line_gaining_its_newline_hasnt_changed_line_endings()
    {
        var hidden = HiddenChanges.Between("a", LineEndingKind.None, "a\r\n", LineEndingKind.Crlf);

        Assert.Equal(new HiddenChanges { NewlineAtEnd = true }, hidden);
    }

    [Fact]
    public void A_byte_order_mark_and_the_newline_at_the_end_are_told_from_the_texts()
    {
        Assert.Equal(new HiddenChanges { ByteOrderMark = false }, HiddenChanges.Between("﻿a\n", null, "a\n", null));
        Assert.Equal(new HiddenChanges { ByteOrderMark = true }, HiddenChanges.Between("a\n", null, "﻿a\n", null));
        Assert.Equal(new HiddenChanges { NewlineAtEnd = false }, HiddenChanges.Between("a\nb\n", null, "a\nb", null));
        Assert.Equal(new HiddenChanges { NewlineAtEnd = true }, HiddenChanges.Between("﻿a", null, "﻿a\n", null));
        // An empty file has no last line to end.
        Assert.Null(HiddenChanges.Between("", null, "\n", null));
    }

    [Fact]
    public void Lines_that_changed_only_in_their_whitespace_are_told()
    {
        Assert.Equal(new HiddenChanges { WhitespaceOnly = true }, HiddenChanges.Between("a \n\tb\n", null, "a\n    b\n", null));
        Assert.Null(HiddenChanges.Between("a\nb\n", null, "a\nc\n", null));
        // A blank line added shows.
        Assert.Null(HiddenChanges.Between("a\nb\n", null, "a\n\nb\n", null));
    }

    [Fact]
    public void A_missing_file_or_the_same_text_has_nothing_to_tell()
    {
        Assert.Null(HiddenChanges.Between(null, null, "a\n", LineEndingKind.Lf));
        Assert.Null(HiddenChanges.Between("a\n", LineEndingKind.Lf, null, null));
        Assert.Null(HiddenChanges.Between("a\nb\n", LineEndingKind.Lf, "a\nb\n", LineEndingKind.Lf));
    }

    [Fact]
    public void A_mode_change_says_whether_the_file_is_executable_now()
    {
        Assert.Equal(true, new ModeChange("100644", "100755").NowExecutable);
        Assert.Equal(false, new ModeChange("100755", "100644").NowExecutable);
        Assert.Null(new ModeChange("100644", "120000").NowExecutable);
    }
}
