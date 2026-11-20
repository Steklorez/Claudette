using System.Text.Json.Nodes;
using Deventry.Core.Claude;
using Deventry.Core.Sessions;

namespace Deventry.Core.Tests.Claude;

/// <summary>
/// Each family's alias added to the picker where Claude Code offers the family only as pinned models, as it does signed in
/// with a token from <c>CLAUDE_CODE_OAUTH_TOKEN</c> (DESIGN.md §5, "Model &amp; effort"; §11, "Detecting").
/// </summary>
public sealed class ModelAliasesTests
{
    /// <summary>Claude Code 2.1.328's <c>initialize</c> reply signed in with <c>claude setup-token</c>'s token: its models and account.</summary>
    private const string TokenSignInReply = """
        {
          "models": [
            {"value":"default","resolvedModel":"claude-sonnet-5-5","displayName":"Default (recommended)","description":"Sonnet 5.5 · Efficient for routine tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"sonnet","resolvedModel":"claude-sonnet-5-5","displayName":"Sonnet","description":"Sonnet 5.5 · Efficient for routine tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"claude-fable-5-1[1m]","resolvedModel":"claude-fable-5-1","displayName":"Fable","description":"Fable 5.1 · Most capable for your hardest and longest-running tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high","max"],"supportsAutoMode":true},
            {"value":"opus","resolvedModel":"claude-opus-5-5","displayName":"Opus","description":"Opus 5.5 · Best for everyday, complex tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"haiku","resolvedModel":"claude-haiku-5-5","displayName":"Haiku","description":"Haiku 5.5 · Fastest for quick answers"}
          ],
          "account": {"tokenSource":"CLAUDE_CODE_OAUTH_TOKEN","apiProvider":"firstParty"},
          "current_permission_mode": "default"
        }
        """;

    private static ModelInfo Model(string value, string? resolved = null, params string[] efforts) =>
        new(value, resolved, value, null, efforts.Length > 0, efforts);

    [Fact]
    public void Adds_the_alias_before_a_family_offered_only_pinned()
    {
        var pinned = Model("claude-fable-5-1[1m]", "claude-fable-5-1", "low", "high", "max") with { SupportsAutoMode = true };
        var models = new[] { Model("default", "claude-sonnet-5-5"), Model("sonnet", "claude-sonnet-5-5"), pinned, Model("opus", "claude-opus-5-5") };

        var result = ModelAliases.WithFamilyAliases(models);

        Assert.Equal(["default", "sonnet", "fable", "claude-fable-5-1[1m]", "opus"], result.Select(m => m.Value));
        var fable = result[2];
        Assert.Equal("Fable (newest)", fable.DisplayName);
        Assert.Equal("Claude Code's newest Fable model", fable.Description);
        Assert.Null(fable.ResolvedModel);
        Assert.True(fable.IsNewestAlias);
        Assert.True(fable.SupportsEffort);
        Assert.True(fable.SupportsAutoMode);
        Assert.Equal(["low", "high", "max"], fable.SupportedEffortLevels);
        Assert.Same(pinned, result[3]);
    }

    [Fact]
    public void Adds_nothing_when_every_family_has_its_alias()
    {
        var models = new[] { Model("default", "claude-opus-5-5"), Model("fable", "claude-fable-5-2"), Model("fable[1m]", "claude-fable-5-2[1m]"), Model("opus", "claude-opus-5-5"), Model("haiku", "claude-haiku-5-5") };

        Assert.Same(models, ModelAliases.WithFamilyAliases(models));
    }

    [Fact]
    public void The_default_entry_is_never_read_as_a_family()
    {
        // Default resolves to a Sonnet, but Sonnet isn't offered on its own: nothing to add for it.
        var models = new[] { Model("default", "claude-sonnet-5-5"), Model("opus", "claude-opus-5-5") };

        Assert.Same(models, ModelAliases.WithFamilyAliases(models));
    }

    [Fact]
    public void Adds_one_alias_for_a_family_with_several_pinned_models()
    {
        var models = new[] { Model("claude-opus-5-5", "claude-opus-5-5"), Model("claude-opus-5-5[1m]", "claude-opus-5-5[1m]"), Model("claude-haiku-4-5-20251001") };

        var result = ModelAliases.WithFamilyAliases(models);

        Assert.Equal(["opus", "claude-opus-5-5", "claude-opus-5-5[1m]", "haiku", "claude-haiku-4-5-20251001"], result.Select(m => m.Value));
    }

    [Fact]
    public void Leaves_models_that_arent_a_family_alone()
    {
        var models = new[] { Model("my-gateway-model"), Model("gpt-fake-1") };

        Assert.Same(models, ModelAliases.WithFamilyAliases(models));
    }

    [Theory]
    [InlineData("fable", "fable")]
    [InlineData("fable[1m]", "fable")]
    [InlineData("claude-fable-5-1[1m]", "fable")]
    [InlineData("claude-haiku-4-5-20251001", "haiku")]
    [InlineData("claude-3-5-sonnet-20241022", "sonnet")]
    [InlineData("us.anthropic.claude-opus-5-5-v1:0", "opus")]
    [InlineData("default", null)]
    [InlineData("gpt-fable-1", null)]
    [InlineData(null, null)]
    public void Reads_the_family_of_an_id(string? id, string? family) => Assert.Equal(family, ModelAliases.FamilyOf(id));

    [Theory]
    [InlineData("claude-fable-5-2", "Fable 5.2")]
    [InlineData("claude-fable-5-2[1m]", "Fable 5.2 (1M context)")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-3-5-sonnet-20241022", "Sonnet 3.5")]
    [InlineData("fable", null)]
    [InlineData("claude-fable", null)]
    [InlineData("something-else", null)]
    public void Names_a_model_from_its_id(string id, string? name) => Assert.Equal(name, ModelAliases.NameOf(id));

    [Fact]
    public void Initialize_signed_in_with_a_token_offers_the_newest_fable_and_says_the_variable()
    {
        var result = InitializeResult.Parse(JsonNode.Parse(TokenSignInReply)!.AsObject());

        Assert.Equal(["default", "sonnet", "fable", "claude-fable-5-1[1m]", "opus", "haiku"], result.Models.Select(m => m.Value));
        Assert.Equal("Fable (newest)", result.Models[2].DisplayName);
        Assert.Equal(["low", "medium", "high", "max"], result.Models[2].SupportedEffortLevels);
        Assert.Equal("CLAUDE_CODE_OAUTH_TOKEN", result.Account.TokenSource);
        Assert.Equal("CLAUDE_CODE_OAUTH_TOKEN", result.Account.EnvironmentToken);
    }

    [Fact]
    public void A_cloud_providers_models_are_left_as_they_are()
    {
        var reply = JsonNode.Parse(TokenSignInReply)!.AsObject();
        reply["account"] = new JsonObject { ["apiProvider"] = "bedrock" };

        var result = InitializeResult.Parse(reply);

        Assert.DoesNotContain(result.Models, m => m.Value == "fable");
        Assert.Null(result.Account.EnvironmentToken);
    }

    [Theory]
    // An API key, or a gateway through ANTHROPIC_BASE_URL: Claude Code 2.1.285 listed claude-fable-5-1 without fable.
    [InlineData("""{"apiProvider":"firstParty"}""")]
    [InlineData("""{"tokenSource":"claude.ai","email":"me@example.com","subscriptionType":"max","apiProvider":"firstParty"}""")]
    [InlineData("""{"tokenSource":"CLAUDE_CODE_OAUTH_TOKEN","email":"me@example.com","subscriptionType":"max","apiProvider":"firstParty"}""")]
    // Signed out, as the signed-out fixture's reply.
    [InlineData("{}")]
    public void Any_other_sign_ins_pinned_models_are_left_as_they_are(string account)
    {
        var reply = JsonNode.Parse(TokenSignInReply)!.AsObject();
        reply["account"] = JsonNode.Parse(account);

        var result = InitializeResult.Parse(reply);

        Assert.Equal(["default", "sonnet", "claude-fable-5-1[1m]", "opus", "haiku"], result.Models.Select(m => m.Value));
    }

    [Theory]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN", null, null, "CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN", "me@example.com", "max", null)]
    [InlineData("claude.ai", null, null, null)]
    [InlineData("ANTHROPIC_API_KEY", null, null, null)]
    [InlineData(null, null, null, null)]
    public void A_token_from_the_environment_is_one_without_an_email_or_plan(string? source, string? email, string? plan, string? expected) =>
        Assert.Equal(expected, new AccountInfo(email, null, plan, source, "firstParty").EnvironmentToken);
}
