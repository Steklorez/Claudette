using System.Text.Json.Nodes;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.Protocol;

/// <summary>
/// What a call or a turn wrote to the prompt cache (<c>usage.cache_creation</c>), and where the session's API key comes
/// from (<c>apiKeySource</c>): the prompt cache's lifetime and default (DESIGN.md §6, "Prompt cache").
/// </summary>
public class PromptCacheWritesTests
{
    private static ClaudeMessage Parse(string line) =>
        MessageParser.TryParse(line, out var message, out var error) ? message : throw new InvalidOperationException(error);

    private static IEnumerable<string> Lines(string type) =>
        ProtocolFixture.Load("01-mock-basic").OutputLines.Where(l => JsonNode.Parse(l)!["type"]?.GetValue<string>() == type);

    [Fact]
    public void A_recorded_results_cache_creation_is_read()
    {
        // The mock API caches nothing, so the recording wrote nothing at either lifetime.
        var result = (ResultMessage)Parse(Lines("result").First());

        Assert.Equal(new PromptCacheWrites(0, 0), result.CacheWrites);
        Assert.False(result.CacheWrites!.WroteAny);
        Assert.Null(PromptCache.LifetimeOf(result.CacheWrites));
    }

    [Fact]
    public void Writes_at_an_hour_or_five_minutes_tell_the_lifetime()
    {
        var line = JsonNode.Parse(Lines("result").First())!.AsObject();
        var creation = line["usage"]!["cache_creation"]!.AsObject();

        creation["ephemeral_1h_input_tokens"] = 18_204;
        creation["ephemeral_5m_input_tokens"] = 312;
        var both = (ResultMessage)Parse(line.ToJsonString());
        Assert.Equal(new PromptCacheWrites(312, 18_204), both.CacheWrites);
        Assert.Equal(PromptCache.OneHour, PromptCache.LifetimeOf(both.CacheWrites));

        creation["ephemeral_1h_input_tokens"] = 0;
        var fiveMinutes = (ResultMessage)Parse(line.ToJsonString());
        Assert.True(fiveMinutes.CacheWrites!.WroteAny);
        Assert.Equal(PromptCache.FiveMinutes, PromptCache.LifetimeOf(fiveMinutes.CacheWrites));
    }

    [Fact]
    public void An_assistant_messages_usage_may_break_its_writes_down()
    {
        // The recorded assistant messages' usage has no cache_creation.
        var recorded = (AssistantMessage)Parse(Lines("assistant").First());
        Assert.Null(recorded.CacheWrites);

        var line = JsonNode.Parse(Lines("assistant").First())!.AsObject();
        line["message"]!["usage"]!["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = 4_096 };
        var assistant = (AssistantMessage)Parse(line.ToJsonString());

        Assert.Equal(new PromptCacheWrites(0, 4_096), assistant.CacheWrites);
    }

    [Fact]
    public void Missing_or_odd_counts_read_as_nothing_written()
    {
        Assert.Null(PromptCacheWrites.From(null));
        Assert.Null(PromptCacheWrites.From(new JsonObject { ["input_tokens"] = 10 }));
        Assert.Null(PromptCacheWrites.From(new JsonObject { ["cache_creation"] = null }));
        Assert.Equal(new PromptCacheWrites(0, 0), PromptCacheWrites.From(new JsonObject
        {
            ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = "lots", ["ephemeral_1h_input_tokens"] = -3 },
        }));
    }

    [Fact]
    public void Init_says_where_the_sessions_API_key_comes_from()
    {
        var init = (SystemInitMessage)Parse(Lines("system").First(l => l.Contains("\"subtype\":\"init\"", StringComparison.Ordinal)));
        Assert.Equal("ANTHROPIC_API_KEY", init.ApiKeySource);

        var signedIn = (SystemInitMessage)Parse("""{"type":"system","subtype":"init","session_id":"s","apiKeySource":"none"}""");
        Assert.Equal(PromptCache.NoApiKey, signedIn.ApiKeySource);
        Assert.Null(((SystemInitMessage)Parse("""{"type":"system","subtype":"init","session_id":"s"}""")).ApiKeySource);
    }
}
