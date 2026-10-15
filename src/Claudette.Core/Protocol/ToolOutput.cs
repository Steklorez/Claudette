using System.Globalization;
using System.Text.Json.Nodes;

namespace Claudette.Core.Protocol;

/// <summary>
/// What a tool's <c>tool_use_result</c> says about how it went, for its tile and its window (DESIGN.md §18,
/// "Conversation cards"): the lines a Read read, the files a Grep or Glob found, a fetch's status. Claude Code types these
/// in the Agent SDK's reference (<c>ReadOutput</c>, <c>GrepOutput</c>, <c>GlobOutput</c>, <c>WebFetchOutput</c>); a field
/// that's missing or of another shape is left out.
/// </summary>
public sealed record ToolOutput
{
    /// <summary>A Read's file text, without line numbers; null for an image, a PDF or a notebook.</summary>
    public string? FileContent { get; init; }

    /// <summary>The line a Read's <see cref="FileContent"/> starts at, 1 for the file's start.</summary>
    public int StartLine { get; init; } = 1;

    /// <summary>A Read's lines, or the lines a Grep's content mode matched.</summary>
    public long? Lines { get; init; }

    /// <summary>A Read's file's lines in all, when it read only some of them.</summary>
    public long? TotalLines { get; init; }

    /// <summary>The files a Grep or Glob found.</summary>
    public long? Files { get; init; }

    /// <summary>A Grep's matches, in its count mode.</summary>
    public long? Matches { get; init; }

    /// <summary>A Grep's mode: <c>content</c>, <c>files_with_matches</c> or <c>count</c>.</summary>
    public string? GrepMode { get; init; }

    /// <summary>The files a Grep or Glob lists.</summary>
    public IReadOnlyList<string> Filenames { get; init; } = [];

    /// <summary>A Glob or Grep cut its list short.</summary>
    public bool Truncated { get; init; }

    /// <summary>A WebFetch's HTTP status, and its reason phrase.</summary>
    public int? StatusCode { get; init; }

    public string? StatusText { get; init; }

    /// <summary>The bytes a WebFetch fetched.</summary>
    public long? Bytes { get; init; }

    /// <summary>A Write made the file rather than replacing it: <c>type</c> is <c>create</c>.</summary>
    public bool? Created { get; init; }

    /// <summary>
    /// What the tile says about the result in a few words: "120 lines", "3 files", "12 matches", "200 OK · 14 KB",
    /// "Created". Null when there's nothing to add to its state.
    /// </summary>
    public string? Facts => this switch
    {
        { Created: true } => "Created",
        { Created: false } => "Updated",
        { StatusCode: { } code } => Bytes is { } bytes ? $"{code} {StatusText} · {Size(bytes)}".Replace("  ", " ", StringComparison.Ordinal) : $"{code} {StatusText}".Trim(),
        { GrepMode: "count", Matches: { } matches } => Count(matches, "match", "matches"),
        { GrepMode: "content", Lines: { } lines } => Count(lines, "line", "lines"),
        { Files: { } files } => Count(files, "file", "files") + (Truncated ? "+" : ""),
        { Lines: { } lines, TotalLines: { } total } when total > lines => $"{Count(lines, "line", "lines")} of {total.ToString("N0", CultureInfo.CurrentCulture)}",
        { Lines: { } lines } => Count(lines, "line", "lines"),
        _ => null,
    };

    /// <summary>The typed view of a tool's result, or null when it has none of these fields.</summary>
    public static ToolOutput? From(string toolName, JsonNode? toolUseResult)
    {
        if (toolUseResult is not JsonObject result)
        {
            return null;
        }
        switch (toolName)
        {
            case "Read" when result.GetObject("file") is { } file && result.GetString("type") == "text":
                return new ToolOutput
                {
                    FileContent = file.GetString("content"),
                    StartLine = (int)Math.Max(1, file["startLine"].AsWholeNumber() ?? 1),
                    Lines = file["numLines"].AsWholeNumber(),
                    TotalLines = file["totalLines"].AsWholeNumber(),
                };
            case "Grep":
                return new ToolOutput
                {
                    GrepMode = result.GetString("mode"),
                    Files = result["numFiles"].AsWholeNumber(),
                    Lines = result["numLines"].AsWholeNumber(),
                    Matches = result["numMatches"].AsWholeNumber(),
                    Filenames = result.GetStringList("filenames"),
                };
            case "Glob":
                return new ToolOutput
                {
                    Files = result["numFiles"].AsWholeNumber(),
                    Filenames = result.GetStringList("filenames"),
                    Truncated = result.GetBool("truncated") == true,
                };
            case "WebFetch" when result["code"].AsWholeNumber() is { } code:
                return new ToolOutput
                {
                    StatusCode = (int)code,
                    StatusText = result.GetString("codeText"),
                    Bytes = result["bytes"].AsWholeNumber(),
                };
            case "Write" when result.GetString("type") is "create" or "update":
                return new ToolOutput { Created = result.GetString("type") == "create" };
            default:
                return null;
        }
    }

    private static string Count(long count, string one, string many) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {(count == 1 ? one : many)}";

    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        >= 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => $"{bytes} bytes",
    };
}
