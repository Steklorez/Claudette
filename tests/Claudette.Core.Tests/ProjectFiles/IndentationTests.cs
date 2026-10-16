using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>How a file is indented, so Tab and new lines indent as it does (DESIGN.md §18, "Project files", "The chip").</summary>
public sealed class IndentationTests
{
    [Fact]
    public void A_file_with_no_indented_lines_has_no_style()
    {
        Assert.Null(Indentation.Detect([]));
        Assert.Null(Indentation.Detect(["a", "b", "", "c"]));
        // Blank lines of spaces or tabs say nothing.
        Assert.Null(Indentation.Detect(["a", "    ", "\t", "b"]));
    }

    [Theory]
    [InlineData(4, "class A\n{\n    void B()\n    {\n        C();\n    }\n}")]
    [InlineData(2, "a:\n  b:\n    c: 1\n  d: 2\ne: 3")]
    [InlineData(8, "int main()\n{\n        if (x)\n                y();\n        return 0;\n}")]
    public void Spaces_are_measured_by_the_step_between_lines(int size, string text) =>
        Assert.Equal(new IndentStyle(UseTabs: false, Size: size), Indentation.Detect(text.Split('\n')));

    [Fact]
    public void Blank_lines_dont_break_a_run()
    {
        // Taken as unindented, the blank lines would make the steps 8, 12 and 16 from nothing, and the width 8.
        string[] lines = ["a", "        b", "", "            c", "    ", "                d"];

        Assert.Equal(new IndentStyle(false, 4), Indentation.Detect(lines));
    }

    [Fact]
    public void More_lines_starting_with_a_tab_than_with_spaces_means_tabs()
    {
        string[] lines = ["a", "\tb", "\t\tc", "\td", "    e"];

        var style = Indentation.Detect(lines);

        Assert.Equal(new IndentStyle(UseTabs: true, Size: 4), style);
        Assert.Equal("Tabs", style!.Label);
        Assert.Equal("\t", style.Unit);
    }

    [Fact]
    public void As_many_tabbed_lines_as_spaced_ones_is_spaces()
    {
        string[] lines = ["a", "\tb", "  c"];

        Assert.Equal(new IndentStyle(UseTabs: false, Size: 2), Indentation.Detect(lines));
    }

    [Fact]
    public void A_tie_between_widths_goes_to_4_then_2_then_8()
    {
        // Steps of 2 twice and 4 twice.
        Assert.Equal(4, Indentation.Detect(["a", "  b", "c", "    d", "e"])!.Size);
        // Steps of 2 twice and 8 twice.
        Assert.Equal(2, Indentation.Detect(["a", "  b", "c", "        d", "e"])!.Size);
    }

    [Fact]
    public void Without_a_step_of_2_4_or_8_the_smallest_indent_is_the_width()
    {
        // Steps of 3: not one the widths look for.
        Assert.Equal(new IndentStyle(false, 3), Indentation.Detect(["a", "   b", "      c", "   d"]));
        // A single space is too small to be a width.
        Assert.Equal(new IndentStyle(false, 4), Indentation.Detect(["a", " b", "c"]));
    }

    [Fact]
    public void Only_the_first_lines_are_read()
    {
        var lines = Enumerable.Repeat("\tx", 10).Concat(Enumerable.Repeat("    y", 100));

        Assert.True(Indentation.Detect(lines, maxLines: 10)!.UseTabs);
        Assert.False(Indentation.Detect(lines)!.UseTabs);
    }

    [Theory]
    [InlineData(false, 1, "1 space", " ")]
    [InlineData(false, 2, "2 spaces", "  ")]
    [InlineData(false, 4, "4 spaces", "    ")]
    [InlineData(true, 4, "Tabs", "\t")]
    public void The_chip_writes_the_style_and_Tab_types_its_unit(bool useTabs, int size, string label, string unit)
    {
        var style = new IndentStyle(useTabs, size);

        Assert.Equal((label, unit), (style.Label, style.Unit));
    }
}
