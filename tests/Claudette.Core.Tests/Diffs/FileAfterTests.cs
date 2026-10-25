using System.Text.Json.Nodes;
using Claudette.Core.Diffs;

namespace Claudette.Core.Tests.Diffs;

/// <summary>What a file tool's call left the file holding, from its input and result (DESIGN.md §8, "This session's changes").</summary>
public class FileAfterTests
{
    [Fact]
    public void A_write_leaves_its_content()
    {
        var input = new JsonObject { ["file_path"] = "/p/a.cs", ["content"] = "new\n" };

        Assert.Equal(("new\n", true), FileAfter.Of("Write", input, null, null, baseKnown: false));
    }

    [Fact]
    public void A_write_over_a_file_with_a_byte_order_mark_keeps_it_as_Claude_Code_does()
    {
        // 2.1.302: the model's content has no mark, but Claude Code keeps the file's, and its result's content has it.
        var input = new JsonObject { ["file_path"] = "/p/a.cs", ["content"] = "new\n" };

        Assert.Equal(("﻿new\n", true), FileAfter.Of("Write", input, new JsonObject { ["content"] = "﻿new\n" }, "﻿old\n", baseKnown: true));
        Assert.Equal(("new\n", true), FileAfter.Of("Write", input, new JsonObject { ["content"] = "other\n" }, "old\n", baseKnown: true));
    }

    [Fact]
    public void The_same_lines_are_the_same_whatever_the_line_endings_mark_or_newline_at_the_end()
    {
        Assert.True(FileAfter.Same("a\nb\n", "a\r\nb\r\n"));
        Assert.True(FileAfter.Same("﻿a\nb\n", "a\nb\n"));
        Assert.True(FileAfter.Same("a\nb\n", "a\nb"));
        Assert.False(FileAfter.Same("a\nb\n\n", "a\nb"));
        Assert.False(FileAfter.Same("a \nb\n", "a\nb\n"));
        Assert.False(FileAfter.Same(null, ""));
    }

    [Fact]
    public void An_edit_replaces_the_first_match_or_every_one()
    {
        var once = new JsonObject { ["old_string"] = "x", ["new_string"] = "y" };
        var all = new JsonObject { ["old_string"] = "x", ["new_string"] = "y", ["replace_all"] = true };

        Assert.Equal(("y x\n", true), FileAfter.Of("Edit", once, null, "x x\n", baseKnown: true));
        Assert.Equal(("y y\n", true), FileAfter.Of("Edit", all, null, "x x\n", baseKnown: true));
    }

    [Fact]
    public void An_edit_takes_the_result_over_the_input()
    {
        // Claude Code reports the strings it used, such as with its quotes made to match the file's.
        var input = new JsonObject { ["old_string"] = "say \"hi\"", ["new_string"] = "say \"bye\"" };
        var result = new JsonObject { ["oldString"] = "say “hi”", ["newString"] = "say “bye”", ["originalFile"] = "say “hi”\n" };

        Assert.Equal(("say “bye”\n", true), FileAfter.Of("Edit", input, result, "say “hi”\n", baseKnown: true));
    }

    [Fact]
    public void An_edit_matches_a_file_with_windows_line_endings()
    {
        var input = new JsonObject { ["old_string"] = "one\ntwo", ["new_string"] = "one\nthree" };

        Assert.Equal(("one\r\nthree\r\n", true), FileAfter.Of("Edit", input, null, "one\r\ntwo\r\n", baseKnown: true));
    }

    [Fact]
    public void An_edit_that_doesnt_fit_its_base_falls_back_to_the_patch()
    {
        var input = new JsonObject { ["old_string"] = "not there", ["new_string"] = "b" };
        var result = new JsonObject
        {
            ["structuredPatch"] = JsonNode.Parse("""[{"oldStart":1,"oldLines":3,"newStart":1,"newLines":3,"lines":[" first","-middle","+MIDDLE"," last"]}]"""),
        };

        Assert.Equal(("first\r\nMIDDLE\r\nlast\r\n", true), FileAfter.Of("Edit", input, result, "first\r\nmiddle\r\nlast\r\n", baseKnown: true));
    }

    [Fact]
    public void A_patch_made_to_another_text_isnt_applied()
    {
        var input = new JsonObject { ["old_string"] = "not there", ["new_string"] = "b" };
        var result = new JsonObject
        {
            ["structuredPatch"] = JsonNode.Parse("""[{"oldStart":1,"oldLines":2,"newStart":1,"newLines":2,"lines":[" first","-middle","+MIDDLE"]}]"""),
        };

        Assert.Equal((null, false), FileAfter.Of("Edit", input, result, "first\nsomething else\n", baseKnown: true));
    }

    [Fact]
    public void A_patch_can_add_lines_after_the_last()
    {
        var hunks = JsonNode.Parse("""[{"oldStart":2,"oldLines":1,"newStart":2,"newLines":3,"lines":[" b","+c","+d"]}]""")!.AsArray();

        Assert.Equal("a\nb\nc\nd\n", FileAfter.ApplyPatch("a\nb\n", hunks));
    }

    [Fact]
    public void An_edit_to_an_unknown_base_leaves_an_unknown_file()
    {
        var input = new JsonObject { ["old_string"] = "a", ["new_string"] = "b" };

        Assert.Equal((null, false), FileAfter.Of("Edit", input, null, null, baseKnown: false));
    }

    [Fact]
    public void An_edit_with_nothing_to_replace_creates_the_file()
    {
        var input = new JsonObject { ["old_string"] = "", ["new_string"] = "hello\n" };

        Assert.Equal(("hello\n", true), FileAfter.Of("Edit", input, null, null, baseKnown: true));
    }

    [Fact]
    public void A_multi_edit_makes_its_edits_in_turn()
    {
        var input = new JsonObject
        {
            ["edits"] = new JsonArray(
                new JsonObject { ["old_string"] = "a", ["new_string"] = "b" },
                new JsonObject { ["old_string"] = "b", ["new_string"] = "c", ["replace_all"] = true }),
        };

        Assert.Equal(("c c\n", true), FileAfter.Of("MultiEdit", input, null, "a b\n", baseKnown: true));
    }

    [Fact]
    public void A_notebook_edit_reports_the_whole_notebook_after()
    {
        var result = new JsonObject { ["notebook_path"] = "/p/n.ipynb", ["original_file"] = "{}", ["updated_file"] = "{\"cells\":[]}" };

        Assert.Equal(("{\"cells\":[]}", true), FileAfter.Of("NotebookEdit", new JsonObject(), result, "{}", baseKnown: true));
    }
}
