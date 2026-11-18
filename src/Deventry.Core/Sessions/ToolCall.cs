namespace Deventry.Core.Sessions;

/// <summary>
/// What a tool call does, whichever agent made it (DESIGN.md §18, "Codex, a second agent"): the conversation, Changed
/// files, the step lists, Joulene and the working line go by this rather than by an agent's tool names.
/// </summary>
public enum ToolKind
{
    /// <summary>A tool with no kind of its own: its name and input are shown as they are.</summary>
    Other,

    /// <summary>A shell command.</summary>
    Command,

    /// <summary>Looking at, or stopping, a command running in the background.</summary>
    CommandOutput,

    /// <summary>Watching something for a while (Claude Code's Monitor).</summary>
    Monitor,

    /// <summary>Reading a file.</summary>
    Read,

    /// <summary>Changing a file that's there.</summary>
    Edit,

    /// <summary>Writing a file whole, new or not.</summary>
    Write,

    /// <summary>Deleting a file.</summary>
    Delete,

    /// <summary>Searching files' contents.</summary>
    Search,

    /// <summary>Finding or listing files by name.</summary>
    Find,

    /// <summary>Fetching a page.</summary>
    WebFetch,

    /// <summary>Searching the web.</summary>
    WebSearch,

    /// <summary>A subagent's work.</summary>
    Agent,

    /// <summary>The to-do list.</summary>
    Todo,

    /// <summary>A skill.</summary>
    Skill,

    /// <summary>Questions for the user.</summary>
    Question,

    /// <summary>A plan, for the user to accept.</summary>
    Plan,

    /// <summary>An MCP server's tool.</summary>
    Mcp,

    /// <summary>Looking at an image.</summary>
    ViewImage,
}

/// <summary>
/// A tool call as Deventry shows it: its kind and what that kind needs, read once from the agent's own call
/// (<c>ClaudeToolCalls.From</c> for Claude Code's, Codex's items for Codex's). Fields a kind doesn't use are null.
/// </summary>
/// <param name="Kind">What the call does.</param>
/// <param name="Name">The tool's name, as the conversation labels it: <c>Edit</c>, <c>Bash</c>, <c>mcp__github__search</c>.</param>
public sealed record ToolCall(ToolKind Kind, string Name)
{
    /// <summary>The file it reads or changes.</summary>
    public string? Path { get; init; }

    /// <summary>A command's text, as the user would type it.</summary>
    public string? Command { get; init; }

    /// <summary>What a search looks for, or the names a find matches.</summary>
    public string? Pattern { get; init; }

    /// <summary>Where a search or find looks.</summary>
    public string? Where { get; init; }

    /// <summary>The page a fetch reads.</summary>
    public string? Url { get; init; }

    /// <summary>What a web search asks.</summary>
    public string? Query { get; init; }

    /// <summary>A subagent's, a skill's or a command's short description.</summary>
    public string? Description { get; init; }

    /// <summary>A subagent's instructions, or a fetch's question about its page.</summary>
    public string? Prompt { get; init; }

    /// <summary>The line a read starts at, when it doesn't start at the top.</summary>
    public int? FromLine { get; init; }

    /// <summary>An edit's text before and after, where the call says (a find-and-replace edit).</summary>
    public string? OldText { get; init; }

    public string? NewText { get; init; }

    /// <summary>A write's whole new content.</summary>
    public string? Content { get; init; }

    /// <summary>
    /// A change as a unified diff's hunks (<c>@@ -1 +1 @@</c> and its lines), without file headers, where the agent
    /// reports one rather than the text before and after.
    /// </summary>
    public string? Hunks { get; init; }

    /// <summary>A file the change moves its file to.</summary>
    public string? MovedTo { get; init; }

    /// <summary>A subagent's type, such as <c>Explore</c>.</summary>
    public string? SubagentType { get; init; }

    /// <summary>A skill's name.</summary>
    public string? Skill { get; init; }

    /// <summary>An MCP tool's server.</summary>
    public string? McpServer { get; init; }

    /// <summary>An MCP tool's own name.</summary>
    public string? McpTool { get; init; }

    /// <summary>Changes a file: an edit, a write or a deletion.</summary>
    public bool IsFileChange => Kind is ToolKind.Edit or ToolKind.Write or ToolKind.Delete;

    /// <summary>Runs a command, or looks at one running.</summary>
    public bool IsCommand => Kind is ToolKind.Command;
}
