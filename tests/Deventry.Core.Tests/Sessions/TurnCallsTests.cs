using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Sessions;

/// <summary>A turn's model calls, tool results and compactions (DESIGN.md §18, "Token breakdown", "What's recorded").</summary>
public class TurnCallsTests
{
    private const string Opus = "claude-opus-5-5";
    private const string Haiku = "claude-haiku-5-5";
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_call_is_counted_once_by_message_id_with_its_latest_figures()
    {
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 3, output: 10, cacheRead: 40_000, write1h: 500, searches: 1, fetches: 2), Start);
        calls.Add(Assistant("msg_1", input: 3, output: 250, cacheRead: 40_000, write1h: 500, searches: 1, fetches: 2), Start.AddSeconds(2));

        var log = calls.End(Usage((Opus, new UsageCounts(3, 250, 500, 40_000, 0.05))), Start.AddSeconds(3));

        var call = Assert.Single(log.Calls);
        Assert.Equal(new CallTokens(3, 250, 40_000, 0, 500), call.Tokens);
        Assert.Equal((1, 2), (call.WebSearches, call.WebFetches));
        Assert.Equal((1, "msg_1", Opus, CallKind.Call), (call.Seq, call.MessageId, call.Model, call.Kind));
        // It came first, so it's timed by its first message.
        Assert.Equal(Start, call.At);
        Assert.Equal(0.05, call.CostUsd, 9);
    }

    [Fact]
    public void Cache_writes_are_split_by_their_lifetime_or_taken_for_five_minutes_without_the_split()
    {
        Assert.Equal(new CallTokens(1, 2, 3, 40, 60), CallTokens.From(JsonNode.Parse("""
            {"input_tokens":1,"output_tokens":2,"cache_read_input_tokens":3,"cache_creation_input_tokens":100,
             "cache_creation":{"ephemeral_5m_input_tokens":40,"ephemeral_1h_input_tokens":60}}
            """)!.AsObject()));
        // The mock sends no split: the write's lifetime isn't known.
        Assert.Equal(new CallTokens(1, 2, 3, 100, 0), CallTokens.From(JsonNode.Parse("""
            {"input_tokens":1,"output_tokens":2,"cache_read_input_tokens":3,"cache_creation_input_tokens":100}
            """)!.AsObject()));
        Assert.Null(CallTokens.From(null));
    }

    [Fact]
    public void What_the_turn_spent_beyond_its_calls_is_claude_codes_own_and_the_calls_add_up_to_it()
    {
        // The spike (DESIGN.md §18, "Spike"): modelUsage also counts calls the stream never shows, such as the title on
        // Haiku, and a little fresh input on the main model.
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 3, output: 100, cacheRead: 30_000, write1h: 400), Start);
        calls.Add(Assistant("msg_2", input: 2, output: 300, cacheRead: 30_400, write1h: 900), Start.AddSeconds(5));

        var log = calls.End(Usage((Opus, new UsageCounts(1_205, 400, 1_300, 60_400, 0.20)), (Haiku, new UsageCounts(1_200, 30, 0, 0, 0.002))), Start.AddSeconds(9));

        Assert.Equal(4, log.Calls.Count);
        var opusRest = log.Calls[2];
        Assert.Equal((3, Opus, CallKind.ClaudeCode, (string?)null), (opusRest.Seq, opusRest.Model, opusRest.Kind, opusRest.MessageId));
        Assert.Equal(new CallTokens(1_200, 0, 0, 0, 0), opusRest.Tokens);
        var haiku = log.Calls[3];
        Assert.Equal((Haiku, CallKind.ClaudeCode), (haiku.Model, haiku.Kind));
        Assert.Equal(new CallTokens(1_200, 30, 0, 0, 0), haiku.Tokens);
        Assert.Equal(Start.AddSeconds(9), haiku.At);
        // Each model's calls add up to what the turn spent on it, cost too.
        foreach (var model in (string[])[Opus, Haiku])
        {
            var sum = log.Calls.Where(c => c.Model == model).Aggregate(CallTokens.Zero, (a, c) => a.Plus(c.Tokens));
            var spent = model == Opus ? new UsageCounts(1_205, 400, 1_300, 60_400, 0.20) : new UsageCounts(1_200, 30, 0, 0, 0.002);
            Assert.Equal((spent.Input, spent.Output, spent.CacheWrite, spent.CacheRead), (sum.Input, sum.Output, sum.CacheWrite, sum.CacheRead));
            Assert.Equal(spent.CostUsd, log.Calls.Where(c => c.Model == model).Sum(c => c.CostUsd), 9);
        }
    }

    [Fact]
    public void The_turns_cost_is_shared_between_its_calls_by_their_weighted_tokens()
    {
        var calls = new TurnCalls();
        // Weighted as Anthropic prices them: 1,000 read is 100, 100 written for an hour is 200, 100 out is 500.
        calls.Add(Assistant("msg_1", input: 0, output: 100, cacheRead: 1_000, write1h: 100), Start);
        // 100 fresh input is 100, 100 written for five minutes is 125.
        calls.Add(Assistant("msg_2", input: 100, output: 0, cacheRead: 0, write5m: 100), Start.AddSeconds(1));

        var log = calls.End(Usage((Opus, new UsageCounts(100, 100, 200, 1_000, 1.025))), Start.AddSeconds(2));

        Assert.Equal(2, log.Calls.Count);
        Assert.Equal(0.8, log.Calls[0].CostUsd, 9);
        Assert.Equal(0.225, log.Calls[1].CostUsd, 9);
    }

    [Fact]
    public void A_turn_that_compacted_has_the_compaction_as_its_call_of_its_own()
    {
        var calls = new TurnCalls();
        calls.Compacted(new CompactBoundaryNotice("auto", 967_045), Start);
        calls.Add(Assistant("msg_1", input: 3, output: 200, cacheRead: 0, write1h: 40_000), Start.AddSeconds(30));

        var log = calls.End(Usage((Opus, new UsageCounts(10, 20_200, 40_000, 964_000, 1.0))), Start.AddSeconds(40));

        Assert.Equal(new Compaction(Start, "auto", 967_045), Assert.Single(log.Compactions));
        var compaction = log.Calls[1];
        Assert.Equal(CallKind.Compaction, compaction.Kind);
        Assert.Equal(new CallTokens(7, 20_000, 964_000, 0, 0), compaction.Tokens);
        // The first call after it wrote everything, as it had to: the cache hadn't expired.
        Assert.Equal(CallKind.Call, log.Calls[0].Kind);
    }

    [Fact]
    public void The_first_call_after_the_cache_expired_writes_the_conversation_again()
    {
        var calls = new TurnCalls();
        // A session's first turn writes everything, and has no idle before it.
        calls.Add(Assistant("msg_1", input: 3, output: 50, cacheRead: 0, write1h: 30_000), Start);
        var first = calls.End(Usage((Opus, new UsageCounts(3, 50, 30_000, 0, 0.3))), Start.AddSeconds(1));
        Assert.Equal(CallKind.Call, first.Calls[0].Kind);
        Assert.Null(first.IdleBefore);

        // A short while later the cache is read.
        calls.Add(Assistant("msg_2", input: 3, output: 50, cacheRead: 30_050, write1h: 200), Start.AddMinutes(10));
        var warm = calls.End(Usage((Opus, new UsageCounts(3, 50, 200, 30_050, 0.02))), Start.AddMinutes(10).AddSeconds(1));
        Assert.Equal(CallKind.Call, warm.Calls[0].Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), warm.IdleBefore);

        // An hour and more later, it's written again: the cache had expired.
        var later = Start.AddMinutes(85);
        calls.Add(Assistant("msg_3", input: 3, output: 50, cacheRead: 0, write1h: 30_300), later);
        calls.Add(Assistant("msg_4", input: 3, output: 50, cacheRead: 30_300, write1h: 100), later.AddSeconds(4));
        var cold = calls.End(Usage((Opus, new UsageCounts(6, 100, 30_400, 30_300, 0.35))), later.AddSeconds(5));
        Assert.Equal([CallKind.CacheExpired, CallKind.Call], cold.Calls.Select(c => c.Kind));
        Assert.Equal(TimeSpan.FromMinutes(75), cold.IdleBefore);
    }

    [Fact]
    public void Writing_it_all_again_after_a_clear_or_another_model_is_not_an_expired_cache()
    {
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 3, output: 50, cacheRead: 0, write1h: 30_000), Start);
        calls.End(Usage((Opus, new UsageCounts(3, 50, 30_000, 0, 0.3))), Start.AddSeconds(1));
        calls.Reset();
        calls.Add(Assistant("msg_2", input: 3, output: 50, cacheRead: 0, write1h: 20_000), Start.AddMinutes(1));
        Assert.Equal(CallKind.Call, calls.End(Usage((Opus, new UsageCounts(3, 50, 20_000, 0, 0.2))), Start.AddMinutes(1)).Calls[0].Kind);

        // Each model has a cache of its own.
        calls.Add(Assistant("msg_3", input: 3, output: 50, cacheRead: 0, write1h: 20_000, model: Haiku), Start.AddMinutes(2));
        Assert.Equal(CallKind.Call, calls.End(Usage((Haiku, new UsageCounts(3, 50, 20_000, 0, 0.02))), Start.AddMinutes(2)).Calls[0].Kind);
    }

    [Fact]
    public void Tool_results_are_steps_of_the_call_that_asked_for_them_and_a_subagent_has_its_type()
    {
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 3, output: 80, cacheRead: 10_000, write1h: 100, tools:
        [
            ("toolu_read", "Read", new JsonObject { ["file_path"] = "a.txt" }),
            ("toolu_agent", "Agent", new JsonObject { ["subagent_type"] = "Explore", ["prompt"] = "Find it" }),
        ]), Start);
        calls.AddResults(Results(null, ("toolu_read", new string('x', 401), false)), Start.AddSeconds(1));
        // The subagent's call, and its Grep, which failed.
        calls.Add(Assistant("msg_2", input: 5, output: 20, cacheRead: 0, write5m: 3_000, parent: "toolu_agent", model: Haiku, tools: [("toolu_grep", "Grep", [])]), Start.AddSeconds(2));
        calls.AddResults(Results("toolu_agent", ("toolu_grep", "No such pattern", true)), Start.AddSeconds(3));
        calls.AddResults(Results(null, ("toolu_agent", new string('r', 1200), false)), Start.AddSeconds(4));
        // A result for a call this turn didn't make is left out.
        calls.AddResults(Results(null, ("toolu_other", "?", false)), Start.AddSeconds(5));

        var log = calls.End(Usage((Opus, new UsageCounts(3, 80, 100, 10_000, 0.01)), (Haiku, new UsageCounts(5, 20, 3_000, 0, 0.004))), Start.AddSeconds(6));

        Assert.Equal(
        [
            new ToolStep(1, null, "Read", 101, false, Start.AddSeconds(1)) { ToolUseId = "toolu_read" },
            new ToolStep(2, "toolu_agent", "Grep", 4, true, Start.AddSeconds(3)) { ToolUseId = "toolu_grep" },
            new ToolStep(1, null, "Agent", 300, false, Start.AddSeconds(4)) { ToolUseId = "toolu_agent" },
        ], log.Steps);
        var subagent = log.Calls[1];
        Assert.Equal(("toolu_agent", "Explore", Haiku), (subagent.Agent, subagent.AgentType, subagent.Model));
        Assert.Null(log.Calls[0].AgentType);
    }

    [Fact]
    public void Claude_codes_own_messages_are_not_calls()
    {
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 0, output: 0, cacheRead: 0, model: "<synthetic>"), Start);
        Assert.Empty(calls.End(ModelUsageTotals.Empty, Start).Calls);
    }

    [Theory]
    [InlineData(1000, 750, 1000)]
    // Its long side is brought down to 1,568 first: 1,568 × 1,176 ÷ 750.
    [InlineData(3136, 2352, 2459)]
    public void An_image_counts_as_its_pixels(int width, int height, long tokens) =>
        Assert.Equal(tokens, TurnCalls.TokensOf(new MessageImage("image/png", Png(width, height))));

    [Fact]
    public void An_image_whose_size_cant_be_read_counts_as_the_most_one_can() =>
        Assert.Equal(1600, TurnCalls.TokensOf(new MessageImage("image/png", [0x89, (byte)'P', (byte)'N', (byte)'G'])));

    [Fact]
    public void A_compaction_says_how_big_the_context_was_before()
    {
        var notice = ((SystemMessage)Parse("""{"type":"system","subtype":"compact_boundary","compact_metadata":{"trigger":"auto","pre_tokens":167204}}""")).CompactBoundary;
        Assert.Equal(new CompactBoundaryNotice("auto", 167_204), notice);
        Assert.Equal(new CompactBoundaryNotice("manual"), ((SystemMessage)Parse("""{"type":"system","subtype":"compact_boundary","compact_metadata":{"trigger":"manual"}}""")).CompactBoundary);
    }

    [Fact]
    public async Task A_recorded_turn_with_subagents_has_their_calls_and_its_tools_results()
    {
        // SUBAGENTS against the mock Messages API: two subagents in parallel, one starting a nested one (DESIGN.md §18).
        var transport = new ReplayTransport(ProtocolFixture.Load("12-subagents"));
        await using var session = new ClaudeSession(transport, TimeProvider.System);

        await session.InitializeAsync(TestContext.Current.CancellationToken);
        await session.SendUserMessageAsync("SUBAGENTS", TestContext.Current.CancellationToken);
        var (requested, before) = await session.ReadUntilAsync<PermissionRequested>();
        requested.Request.Allow();
        var (done, after) = await session.ReadUntilAsync<TurnCompleted>();

        var seen = before.Concat(after).OfType<AssistantMessageReceived>().Select(a => a.Message).Where(m => m.MessageId is not null).ToArray();
        var log = done.Calls;
        // Every call the stream showed is there once, the subagents' with their agent.
        Assert.Equal(seen.Select(m => m.MessageId).Distinct().Count(), log.Calls.Count(c => c.Kind != CallKind.ClaudeCode));
        Assert.Contains(log.Calls, c => c.Agent is not null);
        Assert.All(log.Calls.Where(c => c.Agent is not null), c => Assert.Contains(seen, m => m.ParentToolUseId == c.Agent));
        Assert.Contains(log.Steps, s => s.Tool is "Agent" or "Task");
        // And they add up to what the turn spent.
        foreach (var (model, spent) in done.Usage.Models)
        {
            var sum = log.Calls.Where(c => c.Model == model).Aggregate(CallTokens.Zero, (a, c) => a.Plus(c.Tokens));
            Assert.Equal((spent.Input, spent.Output, spent.CacheWrite, spent.CacheRead), (sum.Input, sum.Output, sum.CacheWrite, sum.CacheRead));
        }
    }

    [Fact]
    public void The_turn_so_far_has_its_calls_and_results_as_they_arrive_and_ending_it_takes_them_all()
    {
        var calls = new TurnCalls();
        calls.Add(Assistant("msg_1", input: 3, output: 50, cacheRead: 0, write1h: 30_000), Start);
        calls.End(Usage((Opus, new UsageCounts(3, 50, 30_000, 0, 0.3))), Start.AddSeconds(1));
        var later = Start.AddMinutes(85);

        calls.Add(Assistant("msg_2", input: 3, output: 50, cacheRead: 0, write1h: 30_300, tools: [("toolu_1", "Read", new JsonObject())]), later);
        calls.AddResults(Results(null, ("toolu_1", new string('x', 400), false)), later.AddSeconds(2));
        var soFar = calls.SoFar();

        // Its first call already says the cache had expired; its cost waits for the turn's result.
        var call = Assert.Single(soFar.Calls);
        Assert.Equal((CallKind.CacheExpired, 0.0), (call.Kind, call.CostUsd));
        Assert.Equal(("Read", 100L), (Assert.Single(soFar.Steps).Tool, soFar.Steps[0].Tokens));
        Assert.Equal(TimeSpan.FromMinutes(85), soFar.IdleBefore);

        calls.Add(Assistant("msg_3", input: 3, output: 50, cacheRead: 30_300, write1h: 100), later.AddSeconds(4));
        var ended = calls.End(Usage((Opus, new UsageCounts(6, 100, 30_400, 30_300, 0.35))), later.AddSeconds(5));
        Assert.Equal(["msg_2", "msg_3"], ended.Calls.Select(c => c.MessageId));
        Assert.Empty(calls.SoFar().Calls);
    }

    private static ModelUsageTotals Usage(params (string Model, UsageCounts Counts)[] models) =>
        new(models.ToDictionary(m => m.Model, m => m.Counts));

    private static AssistantMessage Assistant(string id, long input, long output, long cacheRead, long write5m = 0, long write1h = 0, int searches = 0,
        int fetches = 0, string? parent = null, string model = Opus, (string Id, string Name, JsonObject Input)[]? tools = null)
    {
        var content = new JsonArray();
        foreach (var (toolId, name, toolInput) in tools ?? [])
        {
            content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = toolId, ["name"] = name, ["input"] = toolInput });
        }
        return (AssistantMessage)Parse(new JsonObject
        {
            ["type"] = "assistant",
            ["parent_tool_use_id"] = parent,
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = model,
                ["content"] = content,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input,
                    ["output_tokens"] = output,
                    ["cache_read_input_tokens"] = cacheRead,
                    ["cache_creation_input_tokens"] = write5m + write1h,
                    ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = write5m, ["ephemeral_1h_input_tokens"] = write1h },
                    ["server_tool_use"] = new JsonObject { ["web_search_requests"] = searches, ["web_fetch_requests"] = fetches },
                },
            },
        }.ToJsonString());
    }

    private static UserMessage Results(string? parent, params (string Id, string Text, bool IsError)[] results)
    {
        var content = new JsonArray();
        foreach (var (id, text, isError) in results)
        {
            content.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = text, ["is_error"] = isError });
        }
        return (UserMessage)Parse(new JsonObject { ["type"] = "user", ["parent_tool_use_id"] = parent, ["message"] = new JsonObject { ["role"] = "user", ["content"] = content } }.ToJsonString());
    }

    /// <summary>The start of a PNG: its signature and the IHDR chunk with its size.</summary>
    internal static byte[] Png(int width, int height)
    {
        var data = new byte[33];
        ((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R']).CopyTo(data);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    private static ClaudeMessage Parse(string line) =>
        MessageParser.TryParse(line, out var message, out var error) ? message : throw new InvalidOperationException(error);
}
