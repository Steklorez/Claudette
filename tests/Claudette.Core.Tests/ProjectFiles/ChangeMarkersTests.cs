using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>The editor gutter's bars and peeks for Claude's changes (DESIGN.md §18, "Project files", "Claude's changes").</summary>
public sealed class ChangeMarkersTests
{
    [Fact]
    public void A_new_file_is_all_added()
    {
        var markers = ChangeMarkers.Compute(null, "a\nb\nc")!;

        var block = Assert.Single(markers.Blocks);
        Assert.Equal((1, 3, 3), (block.FirstLine, block.LineCount, block.LastLine));
        Assert.Empty(block.OldLines);
        Assert.Equal([LineChange.Added, LineChange.Added, LineChange.Added], Enumerable.Range(1, 3).Select(markers.KindOf));
        Assert.Null(markers.KindOf(4));
    }

    [Theory]
    [InlineData("a\nb\n", "a\nb\n")]
    // Line endings, and a newline at the end, aren't changes.
    [InlineData("a\r\nb\r\n", "a\nb")]
    [InlineData(null, "")]
    public void The_same_lines_have_no_changes(string? before, string text)
    {
        var markers = ChangeMarkers.Compute(before, text)!;

        Assert.True(markers.IsEmpty);
        Assert.Null(markers.KindOf(1));
        Assert.False(markers.HasRemovalAbove(1));
    }

    [Fact]
    public void A_changed_line_is_modified()
    {
        var markers = ChangeMarkers.Compute("a\nb\nc\n", "a\nB\nc\n")!;

        var block = Assert.Single(markers.Blocks);
        Assert.Equal((2, 1), (block.FirstLine, block.LineCount));
        Assert.Equal(["b"], block.OldLines);
        Assert.Equal([null, LineChange.Modified, null], Enumerable.Range(1, 3).Select(markers.KindOf));
    }

    [Fact]
    public void Past_as_many_lines_as_it_removed_a_change_adds_lines()
    {
        var markers = ChangeMarkers.Compute("a\nb\nc\n", "a\nB1\nB2\nB3\nc\n")!;

        var block = Assert.Single(markers.Blocks);
        Assert.Equal((2, 3), (block.FirstLine, block.LineCount));
        Assert.Equal(["b"], block.OldLines);
        Assert.Equal([LineChange.Modified, LineChange.Added, LineChange.Added], Enumerable.Range(2, 3).Select(markers.KindOf));
        Assert.Null(markers.KindOf(5));
    }

    [Fact]
    public void Fewer_lines_than_it_replaced_are_all_modified_with_no_removal_marked()
    {
        var markers = ChangeMarkers.Compute("a\nb\nc\nd\n", "a\nX\nd\n")!;

        var block = Assert.Single(markers.Blocks);
        Assert.Equal((2, 1), (block.FirstLine, block.LineCount));
        Assert.Equal(["b", "c"], block.OldLines);
        Assert.Equal(LineChange.Modified, markers.KindOf(2));
        Assert.False(markers.HasRemovalAbove(2));
        Assert.False(markers.HasRemovalAbove(3));
    }

    [Fact]
    public void Lines_added_between_others_are_added()
    {
        var markers = ChangeMarkers.Compute("a\nc\n", "a\nb\nc\n")!;

        var block = Assert.Single(markers.Blocks);
        Assert.Equal((2, 1), (block.FirstLine, block.LineCount));
        Assert.Empty(block.OldLines);
        Assert.Equal(LineChange.Added, markers.KindOf(2));
    }

    [Fact]
    public void Lines_only_removed_are_marked_on_the_line_after_them()
    {
        var markers = ChangeMarkers.Compute("a\nb\nc\n", "a\nc\n")!;

        var block = Assert.Single(markers.Blocks);
        Assert.True(block.IsRemovalOnly);
        Assert.Equal((2, 0, 2), (block.FirstLine, block.LineCount, block.LastLine));
        Assert.Equal(["b"], block.OldLines);
        Assert.True(block.Covers(2));
        Assert.False(block.Covers(1));
        Assert.True(markers.HasRemovalAbove(2));
        Assert.False(markers.HasRemovalAbove(1));
        // The line itself isn't changed.
        Assert.Null(markers.KindOf(2));
        Assert.Same(block, markers.BlockAt(2));
    }

    [Theory]
    // The first lines: marked on the new first line.
    [InlineData("a\nb\nc\n", "c\n", 1)]
    // The last lines: marked on the line past the end.
    [InlineData("a\nb\nc\n", "a\n", 2)]
    // Every line.
    [InlineData("a\nb\n", "", 1)]
    public void Lines_removed_at_either_end_are_marked_too(string before, string text, int line)
    {
        var markers = ChangeMarkers.Compute(before, text)!;

        var block = Assert.Single(markers.Blocks);
        Assert.True(block.IsRemovalOnly);
        Assert.Equal(line, block.FirstLine);
        Assert.True(markers.HasRemovalAbove(line));
    }

    [Fact]
    public void Changes_are_found_by_line_and_gone_through_in_either_direction_going_round()
    {
        // Line 2 changed, line 5 removed, and two lines added after line 7.
        var before = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9\nl10\n";
        var text = "l1\nX2\nl3\nl4\nl6\nl7\nl8\nN1\nN2\nl9\nl10\n";
        var markers = ChangeMarkers.Compute(before, text)!;

        Assert.Equal([(2, 1), (5, 0), (8, 2)], markers.Blocks.Select(b => (b.FirstLine, b.LineCount)));
        var (changed, removed, added) = (markers.Blocks[0], markers.Blocks[1], markers.Blocks[2]);

        Assert.Same(changed, markers.BlockAt(2));
        Assert.Same(removed, markers.BlockAt(5));
        Assert.Same(added, markers.BlockAt(9));
        Assert.Null(markers.BlockAt(3));

        Assert.Same(changed, markers.Next(1));
        Assert.Same(removed, markers.Next(2));
        Assert.Same(added, markers.Next(5));
        Assert.Same(changed, markers.Next(8));
        Assert.Same(changed, markers.Next(11));

        Assert.Same(added, markers.Previous(11));
        Assert.Same(removed, markers.Previous(9));
        Assert.Same(removed, markers.Previous(8));
        Assert.Same(changed, markers.Previous(5));
        Assert.Same(added, markers.Previous(2));
        Assert.Same(added, markers.Previous(1));
    }

    [Fact]
    public void With_no_changes_there_is_nowhere_to_go()
    {
        Assert.True(ChangeMarkers.None.IsEmpty);
        Assert.Null(ChangeMarkers.None.Next(1));
        Assert.Null(ChangeMarkers.None.Previous(1));
        Assert.Null(ChangeMarkers.None.BlockAt(1));
    }

    [Fact]
    public void A_file_over_the_line_cap_isnt_compared()
    {
        var atCap = string.Concat(Enumerable.Repeat("x\n", ChangeMarkers.MaxLines));
        var overCap = atCap + "x\n";

        Assert.Single(ChangeMarkers.Compute(atCap, "y\n" + atCap[2..])!.Blocks);
        Assert.Null(ChangeMarkers.Compute("x\n", overCap));
        Assert.Null(ChangeMarkers.Compute(overCap, "x\n"));
    }
}
