using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Claudette.Core.Claude;
using Claudette.Core.Json;
using Claudette.Core.Protocol;

namespace Claudette.Core.Sessions;

/// <summary>
/// One of Claude's tasks as Claude Code keeps it (DESIGN.md §5, "Tasks"): an entry in a <c>TaskList</c> result, or a
/// task's file in its tasks folder (<see cref="ClaudeTaskFolder"/>), which have the same fields.
/// </summary>
/// <param name="Id">Its number, as Claude refers to it: <c>"3"</c>.</param>
/// <param name="Status"><c>pending</c>, <c>in_progress</c> or <c>completed</c>; null when it doesn't say.</param>
/// <param name="BlockedBy">The tasks that have to finish first, by number.</param>
public sealed record ClaudeTask(string Id, string? Subject, string? Description, string? ActiveForm, string? Owner, string? Status, IReadOnlyList<string> BlockedBy)
{
    /// <summary>
    /// The task <paramref name="task"/> describes. Null without an id, or for one Claude Code keeps for itself
    /// (<c>metadata._internal</c>), which it doesn't show Claude or the user either.
    /// </summary>
    public static ClaudeTask? From(JsonObject task)
    {
        if (task.GetStringOrNumber("id") is not { Length: > 0 } id || (task["metadata"] as JsonObject)?["_internal"] is JsonValue hidden && IsTruthy(hidden))
        {
            return null;
        }
        return new ClaudeTask(id, task.GetStringOrNumber("subject"), task.GetStringOrNumber("description"), task.GetStringOrNumber("activeForm"),
            task.GetStringOrNumber("owner"), task.GetStringOrNumber("status"),
            [.. (task["blockedBy"] as JsonArray ?? []).Select(b => b.AsStringOrNumber()).OfType<string>()]);
    }

    private static bool IsTruthy(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => value.GetValue<string>().Length > 0,
        JsonValueKind.Number => value.GetValue<double>() != 0,
        _ => false,
    };
}

/// <summary>
/// The task list Claude Code keeps on disk (DESIGN.md §5, "Tasks"): a folder in <c>tasks</c> in its config folder, named
/// for the session, or for <c>CLAUDE_CODE_TASK_LIST_ID</c> when that's set, which shares one list between sessions. The
/// folder is documented; what's in it isn't (checked against 2.1.301): a <c>&lt;number&gt;.json</c> for each task, with
/// the fields of a <c>TaskList</c> result, and Claude Code's own <c>.lock</c> and <c>.highwatermark</c>, never read.
/// </summary>
public static partial class ClaudeTaskFolder
{
    /// <summary>Names the list a session uses, in place of its own.</summary>
    public const string ListVariable = "CLAUDE_CODE_TASK_LIST_ID";

    /// <summary>
    /// The folder of the list named <paramref name="listId"/> under <paramref name="configDirectory"/>. Claude Code
    /// makes the name safe for a folder by turning anything but letters, digits, <c>_</c> and <c>-</c> into <c>-</c>.
    /// </summary>
    public static string Folder(string configDirectory, string listId) => Path.Combine(configDirectory, "tasks", Unsafe().Replace(listId, "-"));

    /// <summary>The list a session uses: <paramref name="variable"/> (<see cref="ListVariable"/>) when set, else its own.</summary>
    public static string ListId(string sessionId, string? variable) => string.IsNullOrEmpty(variable) ? sessionId : variable;

    /// <summary>
    /// <see cref="ListVariable"/> for a <c>claude</c> started with <paramref name="environment"/> in a folder whose
    /// settings files are <paramref name="settingsFiles"/>, most specific first (<see cref="ClaudeSettingsFiles.ForSession"/>):
    /// the most specific file's <c>env</c> that sets it outranks the environment, as Claude Code applies them. Null when
    /// it's unset or empty.
    /// </summary>
    public static string? ListVariableFor(IReadOnlyDictionary<string, string> environment, IEnumerable<string> settingsFiles)
    {
        foreach (var file in settingsFiles)
        {
            if ((ClaudeSettingsFiles.Read(file)?["env"] as JsonObject)?[ListVariable].AsStringOrNumber() is { } value)
            {
                return value.Length > 0 ? value : null;
            }
        }
        return environment.TryGetValue(ListVariable, out var set) && set.Length > 0 ? set : null;
    }

    /// <summary>
    /// The tasks in <paramref name="folder"/>, by number. Null when there's no folder, or when one of its files can't be
    /// read as a task: Claude Code writes them in place, so a read can catch one half written, and a list read then
    /// would seem to have lost it. The next change to the folder is read again.
    /// </summary>
    public static IReadOnlyList<ClaudeTask>? Read(string folder)
    {
        var tasks = new List<ClaudeTask>();
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                if (Path.GetFileName(file).StartsWith('.'))
                {
                    continue;
                }
                if (JsonTree.ParseObject(File.ReadAllText(file)) is not { } entry)
                {
                    return null;
                }
                // One without a number, or one Claude Code keeps for itself, isn't on the list.
                if (ClaudeTask.From(entry) is { } task)
                {
                    tasks.Add(task);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Being written, or gone since the folder was listed.
            return null;
        }
        return [.. tasks.OrderBy(t => long.TryParse(t.Id, out var number) ? number : long.MaxValue).ThenBy(t => t.Id, StringComparer.Ordinal)];
    }

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex Unsafe();
}
