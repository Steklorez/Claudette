using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.App.Conversation;

/// <summary>
/// What a running tool call is doing, in a few words for the working line (DESIGN.md §5, "Working line"):
/// "Running dotnet test", "Reading TabView.axaml", "Searching for auth".
/// </summary>
public static class ToolActivity
{
    /// <summary>Longer phrases are cut with an ellipsis, so the line stays on one row.</summary>
    public const int MaxLength = 60;

    /// <summary>One of Claude Code's calls, by its tool's name and input.</summary>
    public static string Describe(string name, JsonObject input) => Describe(ClaudeToolCalls.From(name, input));

    /// <summary>One call.</summary>
    public static string Describe(ToolCall call)
    {
        var text = call.Kind switch
        {
            ToolKind.Command => FirstLine(call.Command) is { } command ? $"Running {command}" : "Running a command",
            ToolKind.Read => FileName(call.Path) is { } file ? $"Reading {file}" : "Reading a file",
            ToolKind.Edit => FileName(call.Path) is { } file ? $"Editing {file}" : "Editing a file",
            ToolKind.Write => FileName(call.Path) is { } file ? $"Writing {file}" : "Writing a file",
            ToolKind.Delete => FileName(call.Path) is { } file ? $"Deleting {file}" : "Deleting a file",
            ToolKind.Search => Clean(call.Pattern) is { } pattern ? $"Searching for {pattern}" : "Searching",
            ToolKind.Find => Clean(call.Pattern) is { } pattern ? $"Finding {pattern}" : "Finding files",
            ToolKind.WebFetch => call.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"Fetching {uri.Host}" : "Fetching a page",
            ToolKind.WebSearch => Clean(call.Query) is { } query ? $"Searching the web for {query}" : "Searching the web",
            ToolKind.Agent => Clean(call.Description) is { } description ? $"Running an agent: {description}" : "Running an agent",
            ToolKind.Todo when call.Name is "TodoWrite" or "update_plan" => "Updating the to-do list",
            ToolKind.Skill => Clean(call.Skill) is { } skill ? $"Using the {skill} skill" : "Using a skill",
            ToolKind.Monitor => "Checking on a background command",
            ToolKind.CommandOutput when call.Name == "BashOutput" => "Checking on a background command",
            ToolKind.ViewImage => FileName(call.Path) is { } file ? $"Looking at {file}" : "Looking at an image",
            ToolKind.Mcp when call.McpServer is { } server && call.McpTool is { } tool => $"Using {tool} ({server})",
            _ => $"Using {call.Name}",
        };
        return Cut(text);
    }

    /// <summary>Several of Claude Code's calls, by their tools' names and inputs.</summary>
    public static string? Describe(IReadOnlyList<(string Name, JsonObject Input)> running) =>
        Describe([.. running.Select(r => ClaudeToolCalls.From(r.Name, r.Input))]);

    /// <summary>
    /// Several calls at once, newest last: the newest, and how many others are running. Agents are counted together,
    /// since a fan-out starts several at once.
    /// </summary>
    public static string? Describe(IReadOnlyList<ToolCall> running)
    {
        if (running.Count == 0)
        {
            return null;
        }
        var agents = running.Count(r => r.Kind == ToolKind.Agent);
        if (agents > 1 && agents == running.Count)
        {
            return $"Running {agents} agents";
        }
        var newest = Describe(running[^1]);
        return running.Count > 1 ? Cut(newest, $" and {running.Count - 1} more") : newest;
    }

    /// <summary>The tooltip's lines for several of Claude Code's calls, by their tools' names and inputs.</summary>
    public static string? Details(IReadOnlyList<(string Name, JsonObject Input)> running) =>
        Details([.. running.Select(r => ClaudeToolCalls.From(r.Name, r.Input))]);

    /// <summary>
    /// The running calls in full, one per line, for the working line's tooltip: a command as it was given, a file's
    /// whole path. Null when none is running.
    /// </summary>
    public static string? Details(IReadOnlyList<ToolCall> running)
    {
        if (running.Count == 0)
        {
            return null;
        }
        return string.Join("\n", running.Select(r =>
        {
            var full = r.Kind == ToolKind.Command && r.Command is { } command ? command.Trim() : ToolUseItem.Summarize(r, int.MaxValue);
            return full.Length > 0 ? $"{r.Name}: {full}" : r.Name;
        }));
    }

    /// <summary>The command's first line.</summary>
    private static string? FirstLine(string? command) =>
        command?.Trim().ReplaceLineEndings("\n").Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : null;

    /// <summary>Just the file's name: the full path is on its card.</summary>
    private static string? FileName(string? path) =>
        Clean(path) is { } clean && Path.GetFileName(clean.Replace('\\', '/').TrimEnd('/')) is { Length: > 0 } name ? name : null;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.ReplaceLineEndings(" ");

    private static string Cut(string text, string suffix = "")
    {
        var room = MaxLength - suffix.Length;
        return (text.Length > room ? text[..(room - 1)].TrimEnd() + "…" : text) + suffix;
    }
}
