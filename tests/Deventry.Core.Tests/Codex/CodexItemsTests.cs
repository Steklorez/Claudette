using System.Text.Json.Nodes;
using Deventry.Core.Codex;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.Core.Tests.Codex;

/// <summary>Codex's items as tool calls (DESIGN.md §18, "Codex, a second agent").</summary>
public sealed class CodexItemsTests
{
    [Theory]
    [InlineData("\"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'git status --short'", "git status --short")]
    [InlineData("powershell.exe -NoProfile -Command \"dotnet test\"", "dotnet test")]
    [InlineData("/bin/bash -lc 'ls -la'", "ls -la")]
    [InlineData("zsh -c \"make\"", "make")]
    [InlineData("git status", "git status")]
    public void A_command_is_shown_without_the_shell_Codex_runs_it_in(string command, string shown) =>
        Assert.Equal(shown, CodexItems.Unwrap(command));

    [Fact]
    public void A_search_and_a_listing_are_their_own_kinds()
    {
        var search = CodexItems.CallsOf(Command(new JsonObject { ["type"] = "search", ["command"] = "rg TODO src", ["query"] = "TODO", ["path"] = "src" })).Single();
        var list = CodexItems.CallsOf(Command(new JsonObject { ["type"] = "listFiles", ["command"] = "ls src", ["path"] = "src" })).Single();
        var other = CodexItems.CallsOf(Command(new JsonObject { ["type"] = "unknown", ["command"] = "dotnet build" })).Single();

        Assert.Equal((ToolKind.Search, "TODO", "src"), (search.Call.Kind, search.Call.Pattern, search.Call.Where));
        Assert.Equal((ToolKind.Find, "src"), (list.Call.Kind, list.Call.Where));
        Assert.Equal((ToolKind.Command, "dotnet build"), (other.Call.Kind, other.Call.Command));
        // The labels aren't Claude Code's tools' names, which mean Claude Code's inputs.
        Assert.Equal(CodexItems.Shell, other.Name);
    }

    [Fact]
    public void Deventrys_send_message_tool_is_a_send_message_call_and_others_are_their_own()
    {
        var send = CodexItems.CallsOf(new JsonObject { ["type"] = "dynamicToolCall", ["id"] = "d1", ["tool"] = CodexTools.SendMessage, ["arguments"] = new JsonObject { ["to"] = "Art page", ["message"] = "Go" } }).Single();
        var other = CodexItems.CallsOf(new JsonObject { ["type"] = "dynamicToolCall", ["id"] = "d2", ["tool"] = "lookup", ["arguments"] = new JsonObject() }).Single();

        Assert.Equal((Deventry.Core.Threads.SendMessageCall.ToolName, "Art page"), (send.Name, send.Input.GetString("to")));
        Assert.Equal(("lookup", ToolKind.Other), (other.Name, other.Call.Kind));
    }

    [Fact]
    public void A_failed_command_or_one_with_a_nonzero_exit_is_an_error()
    {
        var failed = CodexItems.ResultsOf(new JsonObject { ["type"] = "commandExecution", ["id"] = "e", ["status"] = "completed", ["exitCode"] = 1, ["aggregatedOutput"] = "error" }).Single();

        Assert.True(failed.IsError);
        Assert.Equal(1, failed.ExitCode);
    }

    [Fact]
    public void An_mcp_tool_a_web_search_and_an_image_are_calls()
    {
        var mcp = CodexItems.CallsOf(new JsonObject { ["type"] = "mcpToolCall", ["id"] = "m", ["server"] = "github", ["tool"] = "create_issue", ["arguments"] = new JsonObject { ["title"] = "Bug" } }).Single();
        var web = CodexItems.CallsOf(new JsonObject { ["type"] = "webSearch", ["id"] = "w", ["query"] = "avalonia arc" }).Single();
        var image = CodexItems.CallsOf(new JsonObject { ["type"] = "imageView", ["id"] = "i", ["path"] = "/repo/shot.png" }).Single();

        Assert.Equal((ToolKind.Mcp, "mcp__github__create_issue", "Bug"), (mcp.Call.Kind, mcp.Name, mcp.Input.GetString("title")));
        Assert.Equal((ToolKind.WebSearch, "avalonia arc"), (web.Call.Kind, web.Call.Query));
        Assert.Equal((ToolKind.ViewImage, "/repo/shot.png"), (image.Call.Kind, image.Call.Path));
    }

    [Theory]
    [InlineData("agentMessage")]
    [InlineData("reasoning")]
    [InlineData("userMessage")]
    public void Text_and_messages_arent_calls(string type) =>
        Assert.Empty(CodexItems.CallsOf(new JsonObject { ["type"] = type, ["id"] = "x" }));

    [Theory]
    [InlineData("bypassPermissions", CodexModes.FullAccess)]
    [InlineData("plan", CodexModes.Plan)]
    [InlineData("default", CodexModes.Auto)]
    [InlineData("acceptEdits", CodexModes.Auto)]
    [InlineData(null, CodexModes.Auto)]
    [InlineData(CodexModes.ReadOnly, CodexModes.ReadOnly)]
    public void A_mode_chosen_for_Claude_Code_is_the_nearest_of_Codexs(string? mode, string codex) =>
        Assert.Equal(codex, CodexModes.Normalize(mode));

    private static JsonObject Command(JsonObject action) => new()
    {
        ["type"] = "commandExecution",
        ["id"] = "exec",
        ["command"] = "powershell.exe -Command 'x'",
        ["commandActions"] = new JsonArray(action),
    };
}
