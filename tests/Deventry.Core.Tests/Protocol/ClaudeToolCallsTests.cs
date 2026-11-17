using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.Core.Tests.Protocol;

/// <summary>Claude Code's tool calls as neutral <see cref="ToolCall"/>s (DESIGN.md §18, "Codex, a second agent").</summary>
public sealed class ClaudeToolCallsTests
{
    private static JsonObject Input(string json) => JsonNode.Parse(json)!.AsObject();

    [Theory]
    [InlineData("Bash", ToolKind.Command)]
    [InlineData("PowerShell", ToolKind.Command)]
    [InlineData("BashOutput", ToolKind.CommandOutput)]
    [InlineData("KillShell", ToolKind.CommandOutput)]
    [InlineData("Monitor", ToolKind.Monitor)]
    [InlineData("Read", ToolKind.Read)]
    [InlineData("Edit", ToolKind.Edit)]
    [InlineData("MultiEdit", ToolKind.Edit)]
    [InlineData("NotebookEdit", ToolKind.Edit)]
    [InlineData("Write", ToolKind.Write)]
    [InlineData("Grep", ToolKind.Search)]
    [InlineData("Glob", ToolKind.Find)]
    [InlineData("WebFetch", ToolKind.WebFetch)]
    [InlineData("WebSearch", ToolKind.WebSearch)]
    [InlineData("Agent", ToolKind.Agent)]
    [InlineData("Task", ToolKind.Agent)]
    [InlineData("TodoWrite", ToolKind.Todo)]
    [InlineData("TaskCreate", ToolKind.Todo)]
    [InlineData("Skill", ToolKind.Skill)]
    [InlineData("AskUserQuestion", ToolKind.Question)]
    [InlineData("ExitPlanMode", ToolKind.Plan)]
    [InlineData("mcp__github__create_issue", ToolKind.Mcp)]
    [InlineData("SomethingNew", ToolKind.Other)]
    public void Each_tool_has_its_kind(string name, ToolKind kind) => Assert.Equal(kind, ClaudeToolCalls.KindOf(name));

    [Fact]
    public void A_commands_text_and_description_are_read()
    {
        var call = ClaudeToolCalls.From("Bash", Input("""{"command":"dotnet test","description":"Run the tests"}"""));

        Assert.Equal(ToolKind.Command, call.Kind);
        Assert.Equal("dotnet test", call.Command);
        Assert.Equal("Run the tests", call.Description);
        Assert.False(call.IsFileChange);
    }

    [Fact]
    public void An_edit_has_its_file_and_texts()
    {
        var call = ClaudeToolCalls.From("Edit", Input("""{"file_path":"/repo/a.cs","old_string":"x","new_string":"y"}"""));

        Assert.True(call.IsFileChange);
        Assert.Equal("/repo/a.cs", call.Path);
        Assert.Equal("x", call.OldText);
        Assert.Equal("y", call.NewText);
    }

    [Fact]
    public void A_notebooks_path_is_its_file() =>
        Assert.Equal("/repo/n.ipynb", ClaudeToolCalls.From("NotebookEdit", Input("""{"notebook_path":"/repo/n.ipynb"}""")).Path);

    [Fact]
    public void A_read_says_where_it_starts() =>
        Assert.Equal(40, ClaudeToolCalls.From("Read", Input("""{"file_path":"a.cs","offset":40}""")).FromLine);

    [Fact]
    public void A_subagent_has_its_type_and_prompt()
    {
        var call = ClaudeToolCalls.From("Agent", Input("""{"description":"Look","prompt":"Find the bug","subagent_type":"Explore"}"""));

        Assert.Equal("Explore", call.SubagentType);
        Assert.Equal("Find the bug", call.Prompt);
    }

    [Fact]
    public void An_mcp_tool_has_its_server_and_tool()
    {
        var call = ClaudeToolCalls.From("mcp__claude_ai_github__create_issue", []);

        Assert.Equal("claude_ai_github", call.McpServer);
        Assert.Equal("create_issue", call.McpTool);
    }

    [Fact]
    public void A_blocks_call_is_read_from_its_name_and_input_unless_given()
    {
        var block = new ToolUseBlock("t1", "Grep", Input("""{"pattern":"TODO","path":"src"}"""), []);
        Assert.Equal(new ToolCall(ToolKind.Search, "Grep") { Pattern = "TODO", Where = "src" }, block.Call);

        var given = new ToolCall(ToolKind.Command, "Shell") { Command = "git status" };
        Assert.Same(given, (block with { Call = given }).Call);
    }
}
