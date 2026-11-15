using System.Text.Json.Nodes;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>A tab's turns recorded with their model calls, tools' results and events (DESIGN.md §18, "What's recorded").</summary>
public class CallRecordingTests
{
    private const string Opus = "claude-opus-5-5";

    [Fact]
    public async Task A_turn_is_recorded_with_its_calls_its_tools_results_and_what_claude_code_spent_itself()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = Opus, ["permissionMode"] = "default" });
        h.Transport.Emit(Assistant("msg_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject
        {
            ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = "a.txt" },
        }));
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "toolu_1", ["content"] = new string('x', 800) }),
            },
        });
        h.Transport.Emit(Assistant("msg_2", input: 2, output: 120, cacheRead: 20_300, write1h: 240, new JsonObject { ["type"] = "text", ["text"] = "Done." }));
        // The turn's usage has Claude Code's title on Haiku too, which the stream never showed.
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["result"] = "Done.",
            ["session_id"] = "s1",
            ["modelUsage"] = new JsonObject
            {
                [Opus] = Usage(input: 5, output: 160, cacheRead: 40_300, cacheWrite: 540, cost: 0.04),
                ["claude-haiku-5-5"] = Usage(input: 1_200, output: 20, cacheRead: 0, cacheWrite: 0, cost: 0.0013),
            },
        });

        await TabTestHarness.Eventually(() => tracker.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id).Count == 3, "the turn's calls");
        var turns = tracker.Store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id);
        var turn = Assert.Single(turns.Select(t => t.Turn).Distinct());
        Assert.NotNull(turn);
        var calls = tracker.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id);
        Assert.All(calls, c => Assert.Equal((turn, "s1"), (c.Turn, c.SessionId)));
        Assert.Equal([("msg_1", CallKind.Call), ("msg_2", CallKind.Call), ((string?)null, CallKind.ClaudeCode)], calls.Select(c => (c.MessageId, c.Kind)));
        Assert.Equal((300L, 0L), (calls[0].Write1h, calls[0].Write5m));
        // The calls add up to the turn, cost too.
        Assert.Equal(turns.Sum(t => t.Total), calls.Sum(c => c.Total));
        Assert.Equal(turns.Sum(t => t.CostUsd), calls.Sum(c => c.CostUsd), 9);
        var step = Assert.Single(tracker.Store.GetSteps([turn]));
        Assert.Equal((1, "Read", 200L), (step.CallSeq, step.Tool, step.Tokens));
    }

    [Fact]
    public async Task Clearing_a_tabs_conversation_is_recorded()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.IsSettled && tracker.Store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count == 1, "the turn");

        h.Transport.Emit("""{"type":"conversation_reset","trigger":"clear"}""");

        await TabTestHarness.Eventually(() => tracker.Store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id).Count == 1, "the clear");
        Assert.Equal(UsageEventKind.Cleared, tracker.Store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id)[0].Kind);
    }

    private static JsonObject Assistant(string id, long input, long output, long cacheRead, long write1h, JsonObject content) => new()
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject
        {
            ["id"] = id,
            ["model"] = Opus,
            ["content"] = new JsonArray(content),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_read_input_tokens"] = cacheRead,
                ["cache_creation_input_tokens"] = write1h,
                ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = write1h },
            },
        },
    };

    private static JsonObject Usage(long input, long output, long cacheRead, long cacheWrite, double cost) => new()
    {
        ["inputTokens"] = input,
        ["outputTokens"] = output,
        ["cacheReadInputTokens"] = cacheRead,
        ["cacheCreationInputTokens"] = cacheWrite,
        ["costUSD"] = cost,
    };
}
