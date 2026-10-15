using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.App.Tests;

/// <summary>A tool call in a window of its own, which follows it (DESIGN.md §18, "Conversation cards").</summary>
public class ToolCallWindowTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));

    private ToolUseItem Call(string name, JsonObject input)
    {
        var call = new ToolUseItem("t1", name, input);
        call.Stamp(_time.GetUtcNow(), _time);
        return call;
    }

    [Fact]
    public void A_running_command_counts_its_time_and_its_output_comes_when_it_ends()
    {
        var bash = Call("Bash", new JsonObject { ["command"] = "dotnet test" });
        using var window = new ToolCallWindowViewModel(bash, _time, new InlineDispatcher(), dark: false);
        Assert.True(window.IsCommand);
        Assert.Equal("Bash · dotnet test", window.Title);
        Assert.Equal("Running · 0s", window.StatusLine);

        var said = new List<string>();
        window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolCallWindowViewModel.StatusLine))
            {
                said.Add(window.StatusLine);
            }
        };
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("Running · 3s", said[^1]);

        bash.CompletedAt = _time.GetUtcNow();
        bash.ApplyResult("Passed!", isError: false, JsonNode.Parse("""{"stdout":"Passed!\nTotal 8","stderr":""}"""));

        Assert.Equal("Passed!\nTotal 8", bash.Output);
        Assert.Equal("Done · 3.0s", window.StatusLine);
        // It stops ticking once the call ends.
        var count = said.Count;
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(count, said.Count);
    }

    [Fact]
    public void A_Read_shows_the_file_numbered_from_where_it_started_reading()
    {
        var read = Call("Read", new JsonObject { ["file_path"] = "src/a.cs", ["offset"] = 10 });
        read.ApplyResult("10\tint x;\n11\tint y;", isError: false,
            JsonNode.Parse("""{"type":"text","file":{"filePath":"src/a.cs","content":"int x;\nint y;","numLines":2,"startLine":10,"totalLines":40}}"""));

        using var window = new ToolCallWindowViewModel(read, _time, new InlineDispatcher(), dark: false);

        Assert.True(window.IsFile);
        Assert.Equal([("10", "int x;"), ("11", "int y;")], window.FileLines.Select(l => (l.Number, l.Text)));
        Assert.Equal("2 lines of 40", window.StatusLine);
    }

    [Fact]
    public void A_Grep_shows_its_matches_file_by_file()
    {
        var grep = Call("Grep", new JsonObject { ["pattern"] = "TODO" });
        grep.ApplyResult("""
            src/a.cs:12:// TODO: tidy
            src/a.cs:40:// TODO: test
            C:\repo\b.cs:3:// TODO
            """, isError: false, JsonNode.Parse("""{"mode":"content","numFiles":2,"filenames":[],"numLines":3}"""));

        using var window = new ToolCallWindowViewModel(grep, _time, new InlineDispatcher(), dark: false);

        Assert.True(window.IsSearch);
        Assert.False(window.ShowsSearchText);
        Assert.Collection(window.SearchFiles,
            a =>
            {
                Assert.Equal("src/a.cs", a.Path);
                Assert.Equal([("12", "// TODO: tidy"), ("40", "// TODO: test")], a.Matches.Select(m => (m.Number, m.Text)));
            },
            b => Assert.Equal(("C:\\repo\\b.cs", "3"), (b.Path, Assert.Single(b.Matches).Number)));
    }

    [Fact]
    public void A_Glob_lists_its_files_and_an_unreadable_search_shows_as_text()
    {
        var glob = Call("Glob", new JsonObject { ["pattern"] = "*.cs" });
        glob.ApplyResult("a.cs\nb.cs", isError: false, JsonNode.Parse("""{"numFiles":2,"filenames":["a.cs","b.cs"]}"""));
        using var listed = new ToolCallWindowViewModel(glob, _time, new InlineDispatcher(), dark: false);
        Assert.Equal(["a.cs", "b.cs"], listed.SearchFiles.Select(f => f.Path));

        var grep = Call("Grep", new JsonObject { ["pattern"] = "x" });
        grep.ApplyResult("No matches found", isError: false, null);
        using var plain = new ToolCallWindowViewModel(grep, _time, new InlineDispatcher(), dark: false);
        Assert.True(plain.ShowsSearchText);
        Assert.Equal("No matches found", plain.ResultText);
    }

    [Fact]
    public void Any_other_tool_shows_its_arguments_and_its_result()
    {
        var mcp = Call("mcp__github__create_issue", new JsonObject { ["title"] = "Bug" });
        mcp.ApplyResult("Created #42", isError: false, null);
        var opened = new List<ToolUseItem>();

        using var window = new ToolCallWindowViewModel(mcp, _time, new InlineDispatcher(), dark: false, opened.Add);

        Assert.True(window.IsOther);
        Assert.Contains("\"title\": \"Bug\"", window.InputText, StringComparison.Ordinal);
        Assert.Equal("Created #42", window.ResultText);
        Assert.False(window.CanOpenDiff);
    }

    [Fact]
    public void An_edit_shows_its_own_change_and_opens_the_file_in_the_diff_view()
    {
        var edit = Call("Edit", new JsonObject { ["file_path"] = "a.cs", ["old_string"] = "x", ["new_string"] = "y" });
        edit.ApplyResult("Updated.", isError: false, null);
        var opened = new List<ToolUseItem>();

        using var window = new ToolCallWindowViewModel(edit, _time, new InlineDispatcher(), dark: false, opened.Add);
        Assert.True(window.IsDiff);
        Assert.Equal("+1 −1 · Done", window.StatusLine);
        window.OpenDiffCommand.Execute(null);

        Assert.Same(edit, Assert.Single(opened));
    }
}
