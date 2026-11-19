using System.Text.Json.Nodes;
using Deventry.Core.Claude;
using Deventry.Core.Protocol;

namespace Deventry.Core.Sessions;

/// <summary>A model Claude Code or Codex offers, with the effort levels it supports (DESIGN.md §5, "Model &amp; effort").</summary>
public sealed record ModelInfo(
    string Value,
    string? ResolvedModel,
    string DisplayName,
    string? Description,
    bool SupportsEffort,
    IReadOnlyList<string> SupportedEffortLevels)
{
    /// <summary>Auto mode works with this model (DESIGN.md §7). Claude Code leaves the field out for one that it doesn't.</summary>
    public bool SupportsAutoMode { get; init; }

    /// <summary>Codex's own default model (<c>isDefault</c>). Claude Code's is its <c>default</c> entry instead.</summary>
    public bool IsDefault { get; init; }

    /// <summary>The effort Codex starts this model at (<c>defaultReasoningEffort</c>); Claude Code doesn't say.</summary>
    public string? DefaultEffort { get; init; }

    /// <summary>
    /// Deventry added this entry: a family's alias (<c>fable</c>), which Claude Code offered only as pinned models, so the
    /// picker has the family's newest model (<see cref="ModelAliases.WithFamilyAliases"/>). Claude Code resolves it.
    /// </summary>
    public bool IsNewestAlias { get; init; }
}

/// <summary>A slash command, as <c>initialize</c> and <c>system/commands_changed</c> list them (DESIGN.md §5, "Composer").</summary>
/// <param name="Name">Without the leading slash.</param>
public sealed record SlashCommandInfo(string Name, string? Description, string? ArgumentHint)
{
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>One of Claude Code's own commands, rather than a user, project, plugin or MCP one.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Reads a <c>commands</c> array; entries without a name are skipped.</summary>
    public static IReadOnlyList<SlashCommandInfo> ParseList(JsonArray? commands) =>
        commands?.OfType<JsonObject>()
            .Select(c => new SlashCommandInfo(c.GetString("name") ?? "", c.GetString("description"), c.GetString("argumentHint"))
            {
                Aliases = c.GetStringList("aliases"),
                IsBuiltIn = c.GetBool("builtin") ?? false,
            })
            .Where(c => c.Name.Length > 0)
            .ToArray() ?? [];
}

public sealed record AccountInfo(string? Email, string? Organization, string? SubscriptionType, string? TokenSource, string? ApiProvider)
{
    /// <summary>The variable Claude Code reads a long-lived token from (<c>claude setup-token</c>).</summary>
    public const string OAuthTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>
    /// The environment variable Claude Code is signed in with, such as <c>CLAUDE_CODE_OAUTH_TOKEN</c>, when its
    /// <c>initialize</c> reply says no email or plan: Claude Code can't see the plan then, and its list of models can lag
    /// behind (DESIGN.md §11, "Detecting"). Null for any other sign-in.
    /// </summary>
    public string? EnvironmentToken =>
        TokenSource is { } source && source.StartsWith(OAuthTokenVariable, StringComparison.Ordinal)
        && string.IsNullOrEmpty(Email) && string.IsNullOrEmpty(SubscriptionType)
            ? source
            : null;
}

/// <summary>Claude Code's reply to the <c>initialize</c> control request.</summary>
public sealed record InitializeResult(
    IReadOnlyList<ModelInfo> Models,
    IReadOnlyList<SlashCommandInfo> Commands,
    AccountInfo Account,
    string? CurrentPermissionMode,
    JsonObject Raw)
{
    /// <summary>The output style the session uses (<c>output_style</c>), such as <c>default</c> or <c>Explanatory</c>.</summary>
    public string? OutputStyle => Raw.GetString("output_style");

    /// <summary>The output styles the session can use (<c>available_output_styles</c>), built-in and the user's own.</summary>
    public IReadOnlyList<string> AvailableOutputStyles => Raw.GetStringList("available_output_styles");

    /// <summary>
    /// Reads Claude Code's reply. Its models get each family's alias where it offers a family only as pinned models (a
    /// first-party sign-in that can't see the plan, DESIGN.md §5), so everything that lists them offers the newest one.
    /// </summary>
    public static InitializeResult Parse(JsonObject response)
    {
        var models = response.GetArray("models")?.OfType<JsonObject>()
            .Select(m => new ModelInfo(
                m.GetString("value") ?? "",
                m.GetString("resolvedModel"),
                m.GetString("displayName") ?? m.GetString("value") ?? "",
                m.GetString("description"),
                m.GetBool("supportsEffort") ?? false,
                m.GetStringList("supportedEffortLevels"))
            {
                SupportsAutoMode = m.GetBool("supportsAutoMode") ?? false,
            })
            .Where(m => m.Value.Length > 0)
            .ToArray() ?? [];

        var commands = SlashCommandInfo.ParseList(response.GetArray("commands"));

        var account = response.GetObject("account") ?? [];
        // A cloud provider's models are its own, pinned by its account on purpose; aliases are Claude's API's.
        if (account.GetString("apiProvider") is null or "firstParty")
        {
            models = [.. ModelAliases.WithFamilyAliases(models)];
        }
        return new InitializeResult(
            models,
            commands,
            new AccountInfo(
                account.GetString("email"),
                account.GetString("organization"),
                account.GetString("subscriptionType"),
                account.GetString("tokenSource"),
                account.GetString("apiProvider")),
            response.GetString("current_permission_mode"),
            response);
    }
}
