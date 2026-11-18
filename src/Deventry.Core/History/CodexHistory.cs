using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Agents;
using Deventry.Core.Protocol;

namespace Deventry.Core.History;

/// <summary>
/// Codex's past sessions for History (DESIGN.md §18, "Codex, a second agent"), read from the session files Codex keeps in
/// <c>CODEX_HOME/sessions/&lt;year&gt;/&lt;month&gt;/&lt;day&gt;/rollout-…-&lt;id&gt;.jsonl</c>, and their names from
/// <c>CODEX_HOME/session_index.jsonl</c>. The format is Codex's own and undocumented, as Claude Code's transcripts are:
/// read tolerantly, a line at a time, unknown lines passed over.
/// </summary>
public static class CodexHistory
{
    /// <summary>Every session under <paramref name="codexHome"/>, newest first; none when it has none.</summary>
    public static IReadOnlyList<SessionSummary> Scan(string codexHome, CancellationToken cancellationToken = default)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        if (!Directory.Exists(sessions))
        {
            return [];
        }
        var names = ReadNames(Path.Combine(codexHome, "session_index.jsonl"));
        var found = new List<SessionSummary>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Read(file, names) is { } summary)
            {
                found.Add(summary);
            }
        }
        return [.. found.OrderByDescending(s => s.LastActivity)];
    }

    /// <summary>One session file's summary; null when it has no session's start, or can't be read.</summary>
    public static SessionSummary? Read(string file, IReadOnlyDictionary<string, string>? names = null)
    {
        string? id = null, folder = null, version = null, firstPrompt = null;
        var prompts = new List<string>();
        var messages = 0;
        DateTimeOffset? last = null;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0 || TryParse(line) is not { } entry)
                {
                    continue;
                }
                if (entry.GetString("timestamp") is { } stamp && DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at))
                {
                    last = last is { } before && before > at ? before : at;
                }
                var payload = entry.GetObject("payload");
                switch (entry.GetString("type"))
                {
                    case "session_meta" when payload is not null:
                        id ??= payload.GetString("id");
                        folder ??= payload.GetString("cwd");
                        version = payload.GetString("cli_version") ?? version;
                        break;
                    case "response_item" when payload?.GetString("type") == "message":
                        var role = payload.GetString("role");
                        var text = string.Join("\n", payload.GetArray("content")?.OfType<JsonObject>().Select(c => c.GetString("text")).OfType<string>() ?? []);
                        if (role == "user" && IsPrompt(text))
                        {
                            messages++;
                            var oneLine = text.ReplaceLineEndings(" ").Trim();
                            firstPrompt ??= oneLine.Length > HistoryIndex.FirstPromptLength ? oneLine[..HistoryIndex.FirstPromptLength] : oneLine;
                            prompts.Add(oneLine.Length > HistoryIndex.SearchPromptLength ? oneLine[..HistoryIndex.SearchPromptLength] : oneLine);
                        }
                        else if (role == "assistant" && text.Length > 0)
                        {
                            messages++;
                        }
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (id is null)
        {
            return null;
        }
        var lastActivity = last ?? File.GetLastWriteTimeUtc(file);
        var searchText = string.Join("\n", prompts);
        return new SessionSummary(id, file, folder, names?.GetValueOrDefault(id), firstPrompt, messages, lastActivity, null, version,
            searchText.Length > HistoryIndex.SearchTextLength ? searchText[..HistoryIndex.SearchTextLength] : searchText)
        {
            Agent = AgentKinds.Codex,
        };
    }

    /// <summary>The session file Codex keeps for <paramref name="sessionId"/> under <paramref name="codexHome"/>; null when it has none.</summary>
    public static string? Find(string codexHome, string sessionId)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        if (!Directory.Exists(sessions))
        {
            return null;
        }
        try
        {
            return Directory.EnumerateFiles(sessions, $"rollout-*-{sessionId}.jsonl", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where a session file brought from another machine goes, so Codex here finds it and resumes it by its id (DESIGN.md
    /// §18, "The session library"): where Codex already keeps the session, else in <c>sessions</c> under the day it was
    /// brought, named as Codex names its own.
    /// </summary>
    public static string PlaceFor(string codexHome, string sessionId, DateTimeOffset now)
    {
        if (Find(codexHome, sessionId) is { } existing)
        {
            return existing;
        }
        var local = now.ToLocalTime();
        var day = Path.Combine(codexHome, "sessions", local.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
            local.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), local.ToString("dd", System.Globalization.CultureInfo.InvariantCulture));
        return Path.Combine(day, $"rollout-{local.ToString("yyyy-MM-dd'T'HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)}-{sessionId}.jsonl");
    }

    /// <summary>The names <c>thread/name/set</c> gave sessions, by id; the last one wins.</summary>
    public static IReadOnlyDictionary<string, string> ReadNames(string indexFile)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(indexFile))
        {
            return names;
        }
        try
        {
            foreach (var line in File.ReadLines(indexFile))
            {
                if (TryParse(line) is { } entry && entry.GetString("id") is { } id && entry.GetString("thread_name") is { Length: > 0 } name)
                {
                    names[id] = name;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A name is a nicety: the session is listed without it.
        }
        return names;
    }

    /// <summary>
    /// A message the user typed, rather than what Codex adds to the conversation as the user: its environment, and a
    /// project's instructions (<c>AGENTS.md</c>, or the <c>CLAUDE.md</c> it reads in place of one).
    /// </summary>
    private static bool IsPrompt(string text) =>
        text.Length > 0 && !text.StartsWith('<') && !text.StartsWith("# AGENTS.md instructions", StringComparison.Ordinal)
        && !text.StartsWith("# CLAUDE.md instructions", StringComparison.Ordinal);

    private static JsonObject? TryParse(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
