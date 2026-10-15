using System.Text.Json;
using System.Text.Json.Nodes;
using Claudette.Core.Json;

namespace Claudette.Core.Claude;

/// <summary>
/// Claude Code's settings files, as Claudette reads them for the few keys it follows: <c>spinnerVerbs</c>
/// (<see cref="SpinnerVerbs"/>), the permission mode a session starts in (<see cref="StartingPermissionMode"/>) and the
/// prompt cache's lifetime (<see cref="Sessions.PromptCacheConfig"/>).
/// </summary>
public static class ClaudeSettingsFiles
{
    /// <summary>
    /// Where Claude Code looks for settings for a session in <paramref name="projectFolder"/>, most specific first:
    /// the project's local and shared settings, then the user's. Managed settings aren't read.
    /// </summary>
    /// <param name="configDirectory">Claude Code's config folder (<c>claude auth status</c>'s <c>configDirectory</c>); null skips the user's file.</param>
    /// <param name="projectFolder">The tab's folder; null skips the project's files, as a tab started without them does.</param>
    public static IReadOnlyList<string> ForSession(string? configDirectory, string? projectFolder)
    {
        var files = new List<string>();
        if (projectFolder is not null)
        {
            files.Add(Path.Combine(projectFolder, ".claude", "settings.local.json"));
            files.Add(Path.Combine(projectFolder, ".claude", "settings.json"));
        }
        if (configDirectory is not null)
        {
            files.Add(Path.Combine(configDirectory, "settings.json"));
        }
        return files;
    }

    /// <summary>
    /// The file's top-level object, read with comments and trailing commas allowed. Null when the file is missing,
    /// unreadable or not a JSON object, which Claude Code skips in a <c>-p</c> run too.
    /// </summary>
    public static JsonObject? Read(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonTree.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
