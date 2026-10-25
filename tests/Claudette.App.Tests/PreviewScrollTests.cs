using Claudette.App.Editor;

namespace Claudette.App.Tests;

/// <summary>A Markdown file's editor and preview scrolling together (DESIGN.md §18, "Markdown preview", "Scrolling together").</summary>
public class PreviewScrollTests
{
    /// <summary>
    /// A heading on line 1, 20 pixels high at 0; a paragraph on lines 3 to 6, 80 high at 30; a table's row on line 8 in two
    /// cells side by side; and a code block on lines 10 to 14 at 200.
    /// </summary>
    private static readonly PreviewScroll Map = new(
    [
        new(1, 1, 0, 20),
        new(3, 6, 30, 80),
        new(8, 8, 150, 20),
        new(8, 8, 150, 20),
        new(10, 14, 200, 100),
    ]);

    [Fact]
    public void Within_a_block_the_place_goes_in_proportion_to_its_lines()
    {
        Assert.Equal(0, Map.OffsetFor(1));
        Assert.Equal(30, Map.OffsetFor(3));
        Assert.Equal(50, Map.OffsetFor(4));
        Assert.Equal(60, Map.OffsetFor(4.5));
        Assert.Equal(220, Map.OffsetFor(11));

        Assert.Equal(4.5, Map.LineFor(60));
        Assert.Equal(11, Map.LineFor(220));
    }

    [Fact]
    public void Between_two_blocks_the_blank_lines_share_the_space_between_them()
    {
        // Line 2 is blank, from the heading's bottom at 20 to the paragraph's top at 30.
        Assert.Equal(20, Map.OffsetFor(2));
        Assert.Equal(25, Map.OffsetFor(2.5));
        Assert.Equal(2.5, Map.LineFor(25));
        // Line 7 is between the paragraph's bottom at 110 and the row at 150.
        Assert.Equal(130, Map.OffsetFor(7.5));
    }

    [Fact]
    public void A_tables_other_cells_in_a_row_and_whatever_lies_above_an_earlier_block_are_left_out()
    {
        Assert.Equal(4, Map.Count);
        var nested = new PreviewScroll([new(1, 10, 0, 200), new(3, 3, 40, 20), new(12, 12, 210, 20)]);
        // The block inside the first one's lines is left out, so the first goes in proportion over all of its own.
        Assert.Equal(2, nested.Count);
        Assert.Equal(40, nested.OffsetFor(3));
    }

    [Fact]
    public void Above_the_first_block_and_below_the_last_the_place_stops_at_the_ends()
    {
        var map = new PreviewScroll([new(5, 5, 100, 20), new(9, 12, 200, 40)]);

        Assert.Equal(0, map.OffsetFor(1));
        Assert.Equal(50, map.OffsetFor(3));
        Assert.Equal(240, map.OffsetFor(40));
        Assert.Equal(1, map.LineFor(0));
        Assert.Equal(3, map.LineFor(50));
        Assert.Equal(13, map.LineFor(1000));
    }

    [Fact]
    public void With_nothing_shown_it_stays_at_the_top()
    {
        Assert.Equal(0, PreviewScroll.None.OffsetFor(42));
        Assert.Equal(1, PreviewScroll.None.LineFor(300));
    }
}
