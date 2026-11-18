using System.Text.Json.Nodes;
using Deventry.Core.Agents;
using Deventry.Core.History;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.History;

/// <summary>Codex's past sessions in History (DESIGN.md §18, "Codex, a second agent"), read from its session files.</summary>
public sealed class CodexHistoryTests : IDisposable
{
    private const string Thread = "01a1eb89-43e5-71e3-8bc3-b1e0d1c53852";

    private readonly TempFolder _home = new("deventry-codex-home");

    public void Dispose() => _home.Dispose();

    private string WriteSession(string thread, string day, params string[] lines) =>
        _home.Write($"sessions/{day}/rollout-2026-11-17T09-30-00-{thread}.jsonl", string.Join("\n", lines) + "\n");

    private static string Meta(string thread, string cwd = "/work/starfall") => new JsonObject
    {
        ["timestamp"] = "2026-11-17T09:30:00.000Z",
        ["type"] = "session_meta",
        ["payload"] = new JsonObject { ["id"] = thread, ["cwd"] = cwd, ["cli_version"] = "0.169.0" },
    }.ToJsonString();

    private static string Message(string role, string text, string time = "2026-11-17T09:31:00.000Z") => new JsonObject
    {
        ["timestamp"] = time,
        ["type"] = "response_item",
        ["payload"] = new JsonObject
        {
            ["type"] = "message",
            ["role"] = role,
            ["content"] = new JsonArray(new JsonObject { ["type"] = role == "user" ? "input_text" : "output_text", ["text"] = text }),
        },
    }.ToJsonString();

    [Fact]
    public void A_session_is_its_threads_folder_prompts_and_name()
    {
        var file = WriteSession(Thread, "2026/11/17",
            Meta(Thread),
            Message("user", "<environment_context>\n  <cwd>/work/starfall</cwd>\n</environment_context>"),
            Message("user", "# AGENTS.md instructions for /work/starfall\n\nBe brief."),
            Message("user", "Fix the jump\nin level two", "2026-11-17T09:31:00.000Z"),
            "not json",
            """{"timestamp":"2026-11-17T09:31:30.000Z","type":"event_msg","payload":{"type":"token_count"}}""",
            Message("assistant", "Fixed.", "2026-11-17T09:32:00.000Z"));
        _home.Write("session_index.jsonl", $$"""
            {"id":"{{Thread}}","thread_name":"First name","updated_at":"2026-11-17T09:31:00Z"}
            {"id":"{{Thread}}","thread_name":"Jump fix","updated_at":"2026-11-17T09:33:00Z"}
            """);

        var summary = Assert.Single(CodexHistory.Scan(_home.Path, TestContext.Current.CancellationToken));

        Assert.Equal((Thread, file, "/work/starfall", "Jump fix"), (summary.SessionId, summary.TranscriptPath, summary.Folder, summary.Title));
        Assert.Equal(("Fix the jump in level two", 2, "0.169.0"), (summary.FirstPrompt, summary.MessageCount, summary.ClaudeCodeVersion));
        Assert.Equal(DateTimeOffset.Parse("2026-11-17T09:32:00Z", System.Globalization.CultureInfo.InvariantCulture), summary.LastActivity);
        Assert.Equal(AgentKinds.Codex, summary.Agent);
    }

    [Fact]
    public void Sessions_come_newest_first_and_a_file_without_a_start_is_passed_over()
    {
        const string Older = "01a1eb89-0000-71e3-8bc3-b1e0d1c53852";
        WriteSession(Older, "2026/11/16", Meta(Older), Message("user", "Older", "2026-11-16T10:00:00.000Z"));
        WriteSession(Thread, "2026/11/17", Meta(Thread), Message("user", "Newer", "2026-11-17T10:00:00.000Z"));
        WriteSession("no-start", "2026/11/17", Message("user", "Nothing to resume"));

        var sessions = CodexHistory.Scan(_home.Path, TestContext.Current.CancellationToken);

        Assert.Equal([Thread, Older], sessions.Select(s => s.SessionId));
    }

    [Fact]
    public void A_home_without_sessions_has_none() =>
        Assert.Empty(CodexHistory.Scan(_home.Combine("nowhere"), TestContext.Current.CancellationToken));
}
