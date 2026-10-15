using Claudette.Core.Auth;
using Claudette.Core.Claude;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Sessions;

/// <summary>How long a tab's prompt cache lasts, and when it runs out (DESIGN.md §6, "Prompt cache").</summary>
public sealed class PromptCacheTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    public void Dispose() => _temp.Dispose();

    private IReadOnlyList<string> SettingsFiles => ClaudeSettingsFiles.ForSession(_temp.Combine("config"), _temp.Combine("project"));

    private PromptCacheConfig Config(params (string Name, string Value)[] variables) =>
        PromptCacheConfig.Read(variables.ToDictionary(v => v.Name, v => v.Value), SettingsFiles);

    private static PromptCacheTtl? Resolve(PromptCacheConfig config, TimeSpan? reported = null, ClaudeBilling billing = ClaudeBilling.Subscription, string? model = "claude-opus-5-5") =>
        PromptCache.Resolve(config, model, reported, billing);

    // ---- The lifetime: first match wins ------------------------------------------------------------------------------

    [Fact]
    public void Without_configuration_or_writes_it_is_Claude_Codes_default_for_the_sign_in()
    {
        var config = Config();

        Assert.Equal(new PromptCacheTtl(PromptCache.OneHour, PromptCacheTtlSource.Default), Resolve(config, billing: ClaudeBilling.Subscription));
        Assert.Equal(new PromptCacheTtl(PromptCache.FiveMinutes, PromptCacheTtlSource.Default), Resolve(config, billing: ClaudeBilling.ApiKey));
        Assert.Equal(new PromptCacheTtl(PromptCache.FiveMinutes, PromptCacheTtlSource.Default), Resolve(config, billing: ClaudeBilling.CloudProvider));
    }

    [Fact]
    public void What_the_last_writes_show_comes_before_the_default()
    {
        Assert.Equal(new PromptCacheTtl(PromptCache.FiveMinutes, PromptCacheTtlSource.Reported), Resolve(Config(), reported: PromptCache.FiveMinutes));
        Assert.Equal(new PromptCacheTtl(PromptCache.OneHour, PromptCacheTtlSource.Reported), Resolve(Config(), reported: PromptCache.OneHour, billing: ClaudeBilling.ApiKey));
    }

    [Fact]
    public void Claude_Codes_configuration_comes_first_in_the_order_its_docs_give()
    {
        // ENABLE_PROMPT_CACHING_1H, over what the writes showed.
        Assert.Equal(new PromptCacheTtl(PromptCache.OneHour, PromptCacheTtlSource.OneHourVariable),
            Resolve(Config(("ENABLE_PROMPT_CACHING_1H", "1")), reported: PromptCache.FiveMinutes, billing: ClaudeBilling.ApiKey));

        // promptCacheTtl, over ENABLE_PROMPT_CACHING_1H.
        _temp.Write("config/settings.json", """{ "promptCacheTtl": "5m" }""");
        Assert.Equal(new PromptCacheTtl(PromptCache.FiveMinutes, PromptCacheTtlSource.Setting),
            Resolve(Config(("ENABLE_PROMPT_CACHING_1H", "1")), reported: PromptCache.OneHour));

        // CLAUDE_CODE_PROMPT_CACHE_TTL, over the setting.
        Assert.Equal(new PromptCacheTtl(PromptCache.OneHour, PromptCacheTtlSource.TtlVariable),
            Resolve(Config(("CLAUDE_CODE_PROMPT_CACHE_TTL", "1h"), ("ENABLE_PROMPT_CACHING_1H", "1"))));

        // FORCE_PROMPT_CACHING_5M, over everything.
        Assert.Equal(new PromptCacheTtl(PromptCache.FiveMinutes, PromptCacheTtlSource.ForcedFiveMinutes),
            Resolve(Config(("FORCE_PROMPT_CACHING_5M", "1"), ("CLAUDE_CODE_PROMPT_CACHE_TTL", "1h"), ("ENABLE_PROMPT_CACHING_1H", "1")), reported: PromptCache.OneHour));
    }

    [Fact]
    public void Values_Claude_Code_doesnt_accept_are_passed_over()
    {
        _temp.Write("project/.claude/settings.local.json", """{ "promptCacheTtl": "30m" }""");
        _temp.Write("project/.claude/settings.json", """{ "promptCacheTtl": "1h" }""");

        var config = Config(("CLAUDE_CODE_PROMPT_CACHE_TTL", "1H"), ("FORCE_PROMPT_CACHING_5M", "0"), ("ENABLE_PROMPT_CACHING_1H", "false"));

        // The local file's value isn't one Claude Code takes, so the project's shared file's applies.
        Assert.Equal("1h", config.Setting);
        Assert.Equal(new PromptCacheTtl(PromptCache.OneHour, PromptCacheTtlSource.Setting), Resolve(config, billing: ClaudeBilling.ApiKey));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_switch_is_on_as_Claude_Code_reads_one(string? value, bool on) => Assert.Equal(on, PromptCache.IsOn(value));

    [Fact]
    public void Turning_prompt_caching_off_hides_the_lifetime_for_every_model_or_one_familys()
    {
        Assert.Null(Resolve(Config(("DISABLE_PROMPT_CACHING", "1"), ("CLAUDE_CODE_PROMPT_CACHE_TTL", "1h")), model: "claude-haiku-4-5"));
        Assert.Null(Resolve(Config(("DISABLE_PROMPT_CACHING", "1")), model: null));

        var opusOff = Config(("DISABLE_PROMPT_CACHING_OPUS", "1"));
        Assert.Null(Resolve(opusOff, model: "claude-opus-5-5[1m]"));
        Assert.NotNull(Resolve(opusOff, model: "claude-haiku-4-5"));
        // A model Claudette doesn't know yet only follows DISABLE_PROMPT_CACHING.
        Assert.NotNull(Resolve(opusOff, model: null));

        Assert.Null(Resolve(Config(("DISABLE_PROMPT_CACHING_FABLE", "true")), model: "claude-fable-1"));
        Assert.Null(Resolve(Config(("DISABLE_PROMPT_CACHING_SONNET", "1")), model: "claude-sonnet-4-6"));
        Assert.Null(Resolve(Config(("DISABLE_PROMPT_CACHING_HAIKU", "1")), model: "claude-haiku-4-5"));
        Assert.NotNull(Resolve(Config(("DISABLE_PROMPT_CACHING", "0")), model: "claude-haiku-4-5"));
    }

    // ---- Reading the configuration ------------------------------------------------------------------------------------

    [Fact]
    public void The_most_specific_settings_file_that_sets_promptCacheTtl_wins()
    {
        _temp.Write("config/settings.json", """{ "promptCacheTtl": "1h" }""");
        Assert.Equal("1h", Config().Setting);

        _temp.Write("project/.claude/settings.json", """{ "promptCacheTtl": "5m" }""");
        Assert.Equal("5m", Config().Setting);

        _temp.Write("project/.claude/settings.local.json", """{ "promptCacheTtl": "1h", /* comments are allowed */ }""");
        Assert.Equal("1h", Config().Setting);

        // A file that can't be read is skipped.
        _temp.Write("project/.claude/settings.local.json", """{ "promptCacheTtl": """);
        Assert.Equal("5m", Config().Setting);
    }

    [Fact]
    public void A_settings_files_env_overrides_the_environment_and_an_empty_value_unsets_it()
    {
        _temp.Write("config/settings.json", """{ "env": { "CLAUDE_CODE_PROMPT_CACHE_TTL": "1h", "ENABLE_PROMPT_CACHING_1H": "" } }""");
        _temp.Write("project/.claude/settings.json", """{ "env": { "DISABLE_PROMPT_CACHING_HAIKU": 1 } }""");

        var config = Config(("CLAUDE_CODE_PROMPT_CACHE_TTL", "5m"), ("ENABLE_PROMPT_CACHING_1H", "1"), ("FORCE_PROMPT_CACHING_5M", "1"));

        Assert.Equal("1h", config.Variable("CLAUDE_CODE_PROMPT_CACHE_TTL"));
        Assert.Null(config.Variable("ENABLE_PROMPT_CACHING_1H"));
        // Not set in any file: the environment's.
        Assert.Equal("1", config.Variable("FORCE_PROMPT_CACHING_5M"));
        // A number reads as its text.
        Assert.True(config.IsDisabledFor("claude-haiku-4-5"));
    }

    [Fact]
    public void Without_the_projects_files_only_the_users_are_read()
    {
        _temp.Write("project/.claude/settings.json", """{ "promptCacheTtl": "5m" }""");
        _temp.Write("config/settings.json", """{ "promptCacheTtl": "1h" }""");

        var config = PromptCacheConfig.Read(NoVariables, ClaudeSettingsFiles.ForSession(_temp.Combine("config"), projectFolder: null));

        Assert.Equal("1h", config.Setting);
        Assert.Null(PromptCacheConfig.None.Setting);
        Assert.Null(PromptCacheConfig.None.Variable("DISABLE_PROMPT_CACHING"));
    }

    // ---- How the tab is paid for ----------------------------------------------------------------------------------------

    private static AuthStatus SignedIn(string authMethod, string? apiProvider = "firstParty") =>
        new(true, authMethod, apiProvider, null, null, null, null, null);

    [Fact]
    public void An_API_key_of_any_kind_is_billed_by_the_token()
    {
        var none = PromptCacheConfig.None;

        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(isApiKeyAccount: true, "none", none, SignedIn("claude.ai")));
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, "ANTHROPIC_API_KEY", none, SignedIn("claude.ai")));
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, "/login managed key", none, null));
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, "apiKeyHelper", none, null));
        // Before the session says, the environment it starts with: -p always uses ANTHROPIC_API_KEY.
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, null, Config(("ANTHROPIC_API_KEY", "sk-ant-test")), SignedIn("claude.ai")));
        // A bearer token outranks every other sign-in.
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, "none", Config(("ANTHROPIC_AUTH_TOKEN", "t")), SignedIn("claude.ai")));
        // claude auth status, until the session says otherwise.
        Assert.Equal(ClaudeBilling.ApiKey, PromptCache.BillingOf(false, null, none, SignedIn("api_key")));
        Assert.Equal(ClaudeBilling.Subscription, PromptCache.BillingOf(false, "none", none, SignedIn("api_key")));
    }

    [Fact]
    public void A_claude_ai_sign_in_is_a_subscription_and_a_cloud_provider_is_billed_by_the_token()
    {
        var none = PromptCacheConfig.None;

        Assert.Equal(ClaudeBilling.Subscription, PromptCache.BillingOf(false, "none", none, SignedIn("claude.ai")));
        Assert.Equal(ClaudeBilling.Subscription, PromptCache.BillingOf(false, "none", none, SignedIn("oauth_token")));
        // Not known yet: most likely a subscription.
        Assert.Equal(ClaudeBilling.Subscription, PromptCache.BillingOf(false, null, none, null));
        Assert.Equal(ClaudeBilling.CloudProvider, PromptCache.BillingOf(false, "none", none, SignedIn("third_party", null)));
        Assert.Equal(ClaudeBilling.CloudProvider, PromptCache.BillingOf(false, "none", none, SignedIn("none", "bedrock")));
    }

    // ---- The clock ----------------------------------------------------------------------------------------------------

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static PromptCacheWrites Wrote(long fiveMinutes = 0, long oneHour = 0) => new(fiveMinutes, oneHour);

    [Fact]
    public void The_cache_runs_from_the_main_agents_latest_request_and_counts_down_on_the_clock()
    {
        var time = new FakeTimeProvider(Start);
        var clock = new PromptCacheClock(time);
        Assert.Null(clock.Countdown(PromptCache.OneHour));

        time.Advance(TimeSpan.FromSeconds(30));
        clock.CallEnded(Wrote(oneHour: 4000));
        time.Advance(TimeSpan.FromSeconds(30));
        clock.TurnEnded(Wrote(oneHour: 4000));

        Assert.Equal(Start.AddMinutes(1), clock.RefreshedAt);
        Assert.Equal(PromptCache.OneHour, clock.ReportedLifetime);
        var fresh = clock.Countdown(PromptCache.OneHour)!.Value;
        Assert.Equal(Start.AddMinutes(61), fresh.ExpiresAt);
        Assert.Equal(PromptCache.OneHour, fresh.Remaining);
        Assert.Equal(60, fresh.WholeMinutes);
        Assert.False(fresh.IsEnding);

        // Whole minutes, rounded down: it never promises more than there is.
        time.Advance(TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(1));
        Assert.Equal(52, clock.Countdown(PromptCache.OneHour)!.Value.WholeMinutes);

        // Three minutes left isn't the last three yet; a second later it is.
        time.Advance(TimeSpan.FromMinutes(57) - TimeSpan.FromMinutes(7) - TimeSpan.FromSeconds(1));
        var three = clock.Countdown(PromptCache.OneHour)!.Value;
        Assert.Equal(3, three.WholeMinutes);
        Assert.False(three.IsEnding);
        time.Advance(TimeSpan.FromSeconds(1));
        var ending = clock.Countdown(PromptCache.OneHour)!.Value;
        Assert.True(ending.IsEnding);
        Assert.Equal(2, ending.WholeMinutes);

        time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
        Assert.Equal(0, clock.Countdown(PromptCache.OneHour)!.Value.WholeMinutes);
        Assert.False(clock.Countdown(PromptCache.OneHour)!.Value.IsExpired);

        time.Advance(TimeSpan.FromSeconds(28));
        Assert.False(clock.Countdown(PromptCache.OneHour)!.Value.IsExpired);
        time.Advance(TimeSpan.FromSeconds(1));
        var expired = clock.Countdown(PromptCache.OneHour)!.Value;
        Assert.True(expired.IsExpired);
        Assert.False(expired.IsEnding);
        Assert.Equal(0, expired.WholeMinutes);

        // The same moment with a five-minute lifetime expired long ago.
        Assert.Equal(Start.AddMinutes(6), clock.Countdown(PromptCache.FiveMinutes)!.Value.ExpiresAt);
    }

    [Fact]
    public void A_turn_without_a_request_doesnt_refresh_the_cache()
    {
        var time = new FakeTimeProvider(Start);
        var clock = new PromptCacheClock(time);
        clock.CallEnded(Wrote(fiveMinutes: 100));
        clock.TurnEnded(null);

        // A local command such as /cost makes no request.
        time.Advance(TimeSpan.FromMinutes(2));
        clock.TurnEnded(Wrote());

        Assert.Equal(Start, clock.RefreshedAt);
        Assert.Equal(PromptCache.FiveMinutes, clock.ReportedLifetime);
    }

    [Fact]
    public void The_calls_writes_tell_the_lifetime_before_the_turns_which_can_include_subagents()
    {
        var clock = new PromptCacheClock(new FakeTimeProvider(Start));

        // The main agent's call wrote to the 5-minute cache; a subagent's, counted in the turn's, to the 1-hour one.
        clock.CallEnded(Wrote(fiveMinutes: 2000));
        clock.TurnEnded(Wrote(fiveMinutes: 2000, oneHour: 500));
        Assert.Equal(PromptCache.FiveMinutes, clock.ReportedLifetime);

        // Calls whose usage doesn't break the writes down leave it to the turn's.
        clock.CallEnded(null);
        clock.TurnEnded(Wrote(oneHour: 300));
        Assert.Equal(PromptCache.OneHour, clock.ReportedLifetime);

        // A turn that only read the cache keeps the last lifetime it was written at.
        clock.CallEnded(Wrote());
        clock.TurnEnded(Wrote());
        Assert.Equal(PromptCache.OneHour, clock.ReportedLifetime);
    }

    [Fact]
    public void A_cleared_or_compacted_conversation_forgets_the_cache_and_another_account_its_lifetime_too()
    {
        var time = new FakeTimeProvider(Start);
        var clock = new PromptCacheClock(time);
        clock.CallEnded(Wrote(oneHour: 10));

        // Compacted mid-turn: only a request after it refreshes the cache.
        clock.Forget();
        clock.TurnEnded(Wrote(oneHour: 10));
        Assert.Null(clock.RefreshedAt);
        Assert.Equal(PromptCache.OneHour, clock.ReportedLifetime);

        clock.CallEnded(null);
        Assert.Equal(Start, clock.RefreshedAt);
        clock.Reset();
        Assert.Null(clock.RefreshedAt);
        Assert.Null(clock.ReportedLifetime);
    }

    [Fact]
    public void A_restored_conversation_runs_from_its_last_request()
    {
        var time = new FakeTimeProvider(Start);
        var clock = new PromptCacheClock(time);

        clock.Replayed(Start.AddMinutes(-50), Wrote(oneHour: 100));
        clock.Replayed(Start.AddMinutes(-20), null);
        // Read out of order, an earlier entry doesn't move it back.
        clock.Replayed(Start.AddMinutes(-40), Wrote(fiveMinutes: 100));

        Assert.Equal(Start.AddMinutes(-20), clock.RefreshedAt);
        Assert.Equal(PromptCache.OneHour, clock.ReportedLifetime);
        Assert.Equal(TimeSpan.FromMinutes(40), clock.Countdown(PromptCache.OneHour)!.Value.Remaining);
        // A replayed call isn't one of the turn under way: a turn with no request after it still doesn't refresh.
        clock.TurnEnded(null);
        Assert.Equal(Start.AddMinutes(-20), clock.RefreshedAt);
    }
}
