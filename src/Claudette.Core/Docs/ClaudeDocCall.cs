using System.Text.Json;
using System.Text.Json.Nodes;
using Claudette.Core.Json;
using Claudette.Core.Protocol;

namespace Claudette.Core.Docs;

/// <summary>What a call to the Docs connector did to a doc.</summary>
public enum ClaudeDocChange
{
    Created,
    Edited,
    Commented,
}

/// <summary>
/// A call to claude.ai's Docs connector that made or changed a doc (DESIGN.md §5, "Claude Docs"), read from the call's
/// input and its reply. The connector is claude.ai's, not Claude Code's, and its replies aren't documented (as seen with
/// 2.1.284): a reply's first line is JSON, whose <c>frame</c> has the doc's id (<c>slug</c>) and where it opens
/// (<c>artifactUrl</c>, <c>url</c>), and what the model is to do next follows it. Every call names its doc as a
/// <c>container</c> of kind <c>project</c>: a <c>batch</c> that makes one has <c>create.name</c>, its title, and the
/// others have its <c>id</c>. An edit's reply has no address; a comment is a <c>create</c> of an <c>utterance</c>.
/// </summary>
/// <param name="DocId">The doc's id, which every call on it names.</param>
/// <param name="Url">Where the doc opens, an http(s) address; null when the reply doesn't say, as for an edit.</param>
/// <param name="Title">Its title: only a call that makes the doc says.</param>
public sealed record ClaudeDocCall(string DocId, string? Url, string? Title, ClaudeDocChange Change)
{
    /// <summary>Claude Code names a claude.ai connector's tools <c>mcp__claude_ai_&lt;connector&gt;__&lt;tool&gt;</c>.</summary>
    private const string ConnectorPrefix = "mcp__claude_ai_";

    /// <summary>A claude.ai connector's tool, which could be the Docs connector's: its call is worth keeping until its result.</summary>
    public static bool MayBe(string toolName) => toolName.StartsWith(ConnectorPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Reads a call and the text of its result; null for another tool, a call the connector refused, or one that names
    /// no doc or only reads one.
    /// </summary>
    public static ClaudeDocCall? From(string toolName, JsonObject input, string resultText)
    {
        if (!MayBe(toolName) || input.GetObject("container") is not { } container || container.GetString("kind") != "project")
        {
            return null;
        }
        var reply = Reply(resultText);
        if (!Allowed(reply))
        {
            return null;
        }
        var frame = reply?.GetObject("frame");
        var create = container.GetObject("create");
        var tool = toolName[(toolName.LastIndexOf("__", StringComparison.Ordinal) + 2)..];
        ClaudeDocChange? change = create is not null ? ClaudeDocChange.Created
            : tool == "create" && input.GetString("object") == "utterance" ? ClaudeDocChange.Commented
            : tool is "batch" or "update" ? ClaudeDocChange.Edited
            : null;
        if (change is null || (create is not null && !Allowed(reply?.GetObject("created"))))
        {
            return null;
        }
        var id = frame?.GetString("slug") ?? reply?.GetObject("created")?.GetString("minted") ?? container.GetString("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        var url = WebAddress(frame?.GetString("artifactUrl")) ?? WebAddress(frame?.GetString("url"));
        var title = create?.GetString("name") is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
        return new ClaudeDocCall(id, url, title, change.Value);
    }

    /// <summary>The reply's JSON: its first line, or the whole text when that isn't JSON; null when neither is.</summary>
    private static JsonObject? Reply(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }
        var end = trimmed.IndexOf('\n', StringComparison.Ordinal);
        return (end >= 0 ? Parse(trimmed[..end]) : null) ?? Parse(trimmed);
    }

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonTree.ParseObject(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The connector let it through: its <c>verdict</c> is <c>allow</c>, or it gives none.</summary>
    private static bool Allowed(JsonObject? reply) => reply?.GetString("verdict") is null or "allow";

    private static string? WebAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? address : null;
}
