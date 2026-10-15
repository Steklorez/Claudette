using System.Text.Json.Nodes;
using Claudette.Core.Auth;
using Claudette.Core.Claude;
using Claudette.Core.Protocol;

namespace Claudette.Core.Sessions;

/// <summary>How a tab's requests are paid for, which decides how long Claude Code's prompt cache lasts unless it's set.</summary>
public enum ClaudeBilling
{
    /// <summary>A claude.ai subscription: what Claude reads counts toward the plan's usage limits.</summary>
    Subscription,

    /// <summary>
    /// An API key, billed by the token: one of Claudette's API-key accounts, <c>ANTHROPIC_API_KEY</c> or a bearer token,
    /// a key helper, or the key a Console sign-in stored.
    /// </summary>
    ApiKey,

    /// <summary>A cloud provider, such as Amazon Bedrock or Google Cloud, billed by the token.</summary>
    CloudProvider,
}

/// <summary>What decided a prompt cache's lifetime (<see cref="PromptCache.Resolve"/>), in the order they're tried.</summary>
public enum PromptCacheTtlSource
{
    /// <summary><c>FORCE_PROMPT_CACHING_5M</c>, which takes precedence over everything else.</summary>
    ForcedFiveMinutes,

    /// <summary><c>CLAUDE_CODE_PROMPT_CACHE_TTL</c>.</summary>
    TtlVariable,

    /// <summary>The <c>promptCacheTtl</c> setting.</summary>
    Setting,

    /// <summary><c>ENABLE_PROMPT_CACHING_1H</c>.</summary>
    OneHourVariable,

    /// <summary>The lifetime the latest request that wrote to the cache wrote it at.</summary>
    Reported,

    /// <summary>Claude Code's default for how the tab is paid for (<see cref="ClaudeBilling"/>).</summary>
    Default,
}

/// <summary>How long the prompt cache holds the conversation after each request, and what decided it.</summary>
public sealed record PromptCacheTtl(TimeSpan Lifetime, PromptCacheTtlSource Source);

/// <summary>
/// How long a tab's prompt cache lasts, and when it runs out (DESIGN.md §6, "Prompt cache"). Anthropic's prompt cache
/// keeps a conversation's prefix for a lifetime after each request that reads or writes it: a message sent before then
/// reads the conversation from the cache, at a fraction of the price, and one sent after has it read again in full.
/// </summary>
public static class PromptCache
{
    public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>The last stretch before the cache expires, which the composer's chip shows in the caution color.</summary>
    public static readonly TimeSpan EndingWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The lifetime of the cache for the tab's next request, first match winning: Claude Code's own configuration, in
    /// the order its docs give (<c>FORCE_PROMPT_CACHING_5M</c>, then <c>CLAUDE_CODE_PROMPT_CACHE_TTL</c>, then the
    /// <c>promptCacheTtl</c> setting, then <c>ENABLE_PROMPT_CACHING_1H</c>); then what the latest writes were made at;
    /// then Claude Code's default, an hour for a subscription's main conversation and five minutes otherwise. Null when
    /// Claude Code doesn't cache <paramref name="model"/>'s prompts at all.
    /// </summary>
    /// <param name="model">The model the tab's next request goes to, for the per-model <c>DISABLE_PROMPT_CACHING_*</c>.</param>
    /// <param name="reported">The lifetime the latest writes show (<see cref="PromptCacheClock.ReportedLifetime"/>).</param>
    public static PromptCacheTtl? Resolve(PromptCacheConfig config, string? model, TimeSpan? reported, ClaudeBilling billing)
    {
        if (config.IsDisabledFor(model))
        {
            return null;
        }
        if (IsOn(config.Variable(PromptCacheConfig.ForceFiveMinutesVariable)))
        {
            return new PromptCacheTtl(FiveMinutes, PromptCacheTtlSource.ForcedFiveMinutes);
        }
        if (ParseLifetime(config.Variable(PromptCacheConfig.TtlVariable)) is { } chosen)
        {
            return new PromptCacheTtl(chosen, PromptCacheTtlSource.TtlVariable);
        }
        if (ParseLifetime(config.Setting) is { } set)
        {
            return new PromptCacheTtl(set, PromptCacheTtlSource.Setting);
        }
        if (IsOn(config.Variable(PromptCacheConfig.OneHourVariable)))
        {
            return new PromptCacheTtl(OneHour, PromptCacheTtlSource.OneHourVariable);
        }
        if (reported is { } seen)
        {
            return new PromptCacheTtl(seen, PromptCacheTtlSource.Reported);
        }
        return new PromptCacheTtl(billing == ClaudeBilling.Subscription ? OneHour : FiveMinutes, PromptCacheTtlSource.Default);
    }

    /// <summary>
    /// The lifetime a call's or a turn's writes show: an hour when any went to the 1-hour cache, else five minutes when
    /// any went to the 5-minute one. Null when nothing was written, or the usage didn't say.
    /// </summary>
    public static TimeSpan? LifetimeOf(PromptCacheWrites? writes) => writes switch
    {
        { OneHourTokens: > 0 } => OneHour,
        { FiveMinuteTokens: > 0 } => FiveMinutes,
        _ => null,
    };

    /// <summary>
    /// How a tab's requests are paid for, from what Claudette knows about its sign-in: the tab runs under one of
    /// Claudette's API-key accounts; the session's <c>apiKeySource</c> on <c>system/init</c>; the variables its
    /// <c>claude</c> starts with (a bearer token outranks every other sign-in, and <c>-p</c> always uses
    /// <c>ANTHROPIC_API_KEY</c>); then <c>claude auth status</c>. Anything else is taken for a subscription.
    /// </summary>
    /// <param name="apiKeySource">The session's latest <c>apiKeySource</c>; null before its first turn.</param>
    /// <param name="signIn">The Default account's <c>claude auth status</c>; null before the first.</param>
    public static ClaudeBilling BillingOf(bool isApiKeyAccount, string? apiKeySource, PromptCacheConfig config, AuthStatus? signIn)
    {
        if (isApiKeyAccount
            || config.Variable(PromptCacheConfig.AuthTokenVariable) is not null
            || apiKeySource is null && config.Variable(PromptCacheConfig.ApiKeyVariable) is not null
            || apiKeySource is { Length: > 0 } and not NoApiKey)
        {
            return ClaudeBilling.ApiKey;
        }
        return signIn switch
        {
            { AuthMethod: "third_party" } => ClaudeBilling.CloudProvider,
            { ApiProvider: { Length: > 0 } provider } when provider != "firstParty" => ClaudeBilling.CloudProvider,
            // Only until the session says: one with no API key doesn't run on the key claude auth status found.
            { AuthMethod: "api_key" or "api_key_helper" } when apiKeySource is null => ClaudeBilling.ApiKey,
            _ => ClaudeBilling.Subscription,
        };
    }

    /// <summary><c>apiKeySource</c> for a session that authenticates without an API key.</summary>
    public const string NoApiKey = "none";

    /// <summary><c>5m</c> or <c>1h</c>, the only values Claude Code accepts; null for anything else.</summary>
    public static TimeSpan? ParseLifetime(string? value) => value switch
    {
        "5m" => FiveMinutes,
        "1h" => OneHour,
        _ => null,
    };

    /// <summary>A switch variable that's on, read as Claude Code reads one: <c>1</c>, <c>true</c>, <c>yes</c> or <c>on</c>.</summary>
    public static bool IsOn(string? value) => value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}

/// <summary>
/// What Claude Code's own configuration says about a tab's prompt cache (DESIGN.md §6, "Prompt cache"): the variables
/// of the environment its <c>claude</c> starts with, with the <c>env</c> of its settings files over them as Claude Code
/// applies it, and the <c>promptCacheTtl</c> setting. Read as the tab's session starts.
/// </summary>
public sealed class PromptCacheConfig
{
    /// <summary>Turns prompt caching off for every model, whatever the per-model variables say.</summary>
    public const string DisableVariable = "DISABLE_PROMPT_CACHING";

    /// <summary><c>5m</c> or <c>1h</c> for the main conversation.</summary>
    public const string TtlVariable = "CLAUDE_CODE_PROMPT_CACHE_TTL";

    /// <summary>Asks for the 1-hour lifetime.</summary>
    public const string OneHourVariable = "ENABLE_PROMPT_CACHING_1H";

    /// <summary>Forces the 5-minute lifetime, over everything else.</summary>
    public const string ForceFiveMinutesVariable = "FORCE_PROMPT_CACHING_5M";

    /// <summary>The setting, <c>5m</c> or <c>1h</c>, in any settings file.</summary>
    public const string SettingKey = "promptCacheTtl";

    /// <summary>An API key, which <c>-p</c> always signs in with.</summary>
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>A bearer token, which outranks every other sign-in.</summary>
    public const string AuthTokenVariable = "ANTHROPIC_AUTH_TOKEN";

    /// <summary>
    /// The variables that turn prompt caching off for one family's models: the family, as model ids name it (and the
    /// tab's model is compared by it), and the variable.
    /// </summary>
    public static readonly IReadOnlyList<(string Family, string Variable)> DisableForFamily =
    [
        ("fable", "DISABLE_PROMPT_CACHING_FABLE"),
        ("haiku", "DISABLE_PROMPT_CACHING_HAIKU"),
        ("opus", "DISABLE_PROMPT_CACHING_OPUS"),
        ("sonnet", "DISABLE_PROMPT_CACHING_SONNET"),
    ];

    /// <summary>Every variable read.</summary>
    public static IReadOnlyList<string> Variables { get; } =
    [
        DisableVariable, .. DisableForFamily.Select(f => f.Variable), TtlVariable, OneHourVariable, ForceFiveMinutesVariable,
        ApiKeyVariable, AuthTokenVariable,
    ];

    private readonly IReadOnlyDictionary<string, string> _variables;

    private PromptCacheConfig(IReadOnlyDictionary<string, string> variables, string? setting)
    {
        _variables = variables;
        Setting = setting;
    }

    /// <summary>Nothing set: Claude Code's defaults, until the tab's configuration has been read.</summary>
    public static PromptCacheConfig None { get; } = new(new Dictionary<string, string>(), null);

    /// <summary>
    /// <c>promptCacheTtl</c> from the most specific settings file that sets it to <c>5m</c> or <c>1h</c>; null when none
    /// does.
    /// </summary>
    public string? Setting { get; }

    /// <summary>One of <see cref="Variables"/>, as Claude Code would have it; null when it's unset or empty.</summary>
    public string? Variable(string name) => _variables.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>
    /// Reads the configuration of a <c>claude</c> started with <paramref name="environment"/> in a folder whose settings
    /// files are <paramref name="settingsFiles"/>, most specific first (<see cref="ClaudeSettingsFiles.ForSession"/>).
    /// A settings file's <c>env</c> overrides the environment, the most specific file's winning, and an empty value
    /// unsets a variable, as Claude Code applies them. A file that's missing or not valid JSON is skipped.
    /// </summary>
    public static PromptCacheConfig Read(IReadOnlyDictionary<string, string> environment, IEnumerable<string> settingsFiles)
    {
        var fromFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        string? setting = null;
        foreach (var file in settingsFiles)
        {
            if (ClaudeSettingsFiles.Read(file) is not { } root)
            {
                continue;
            }
            if (setting is null && root[SettingKey].AsStringOrNumber() is { } ttl && PromptCache.ParseLifetime(ttl) is not null)
            {
                setting = ttl;
            }
            if (root["env"] is JsonObject env)
            {
                foreach (var name in Variables)
                {
                    if (!fromFiles.ContainsKey(name) && env[name].AsStringOrNumber() is { } value)
                    {
                        fromFiles[name] = value;
                    }
                }
            }
        }
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Variables)
        {
            if (fromFiles.TryGetValue(name, out var value) || environment.TryGetValue(name, out value))
            {
                variables[name] = value;
            }
        }
        return new PromptCacheConfig(variables, setting);
    }

    /// <summary>
    /// Claude Code doesn't cache prompts for <paramref name="model"/>: <c>DISABLE_PROMPT_CACHING</c> turns it off for
    /// every model, and <c>DISABLE_PROMPT_CACHING_OPUS</c> and the others for the models of one family.
    /// </summary>
    public bool IsDisabledFor(string? model) =>
        PromptCache.IsOn(Variable(DisableVariable))
        || model is not null && DisableForFamily.Any(f => model.Contains(f.Family, StringComparison.OrdinalIgnoreCase) && PromptCache.IsOn(Variable(f.Variable)));
}

/// <summary>
/// When a tab's prompt cache was last refreshed, and the lifetime its latest writes show (DESIGN.md §6, "Prompt
/// cache"). Each of the main agent's requests reads or writes the main conversation's cache, which holds it for its
/// lifetime from then; subagents' requests have caches of their own, so they don't count.
/// </summary>
public sealed class PromptCacheClock(TimeProvider time)
{
    /// <summary>The main agent called the model since the last turn ended.</summary>
    private bool _calledThisTurn;

    /// <summary>One of those calls said what it wrote to the cache.</summary>
    private bool _reportedThisTurn;

    /// <summary>When the main agent's latest request ended; null before the first, or once the cache is forgotten.</summary>
    public DateTimeOffset? RefreshedAt { get; private set; }

    /// <summary>The lifetime of the latest writes to the cache; null until a request wrote any.</summary>
    public TimeSpan? ReportedLifetime { get; private set; }

    /// <summary>One of the main agent's calls ended now: an <c>assistant</c> message from it, with its writes.</summary>
    public void CallEnded(PromptCacheWrites? writes)
    {
        RefreshedAt = time.GetUtcNow();
        _calledThisTurn = true;
        if (PromptCache.LifetimeOf(writes) is { } lifetime)
        {
            ReportedLifetime = lifetime;
            _reportedThisTurn = true;
        }
    }

    /// <summary>
    /// The turn ended now (its <c>result</c>), with what it wrote. It refreshes the cache only when the main agent called
    /// the model in it: a local command such as <c>/cost</c> makes no request. Its writes tell the lifetime only when
    /// none of its calls did, since a turn's can include subagents', whose cache is another.
    /// </summary>
    public void TurnEnded(PromptCacheWrites? writes)
    {
        if (_calledThisTurn)
        {
            RefreshedAt = time.GetUtcNow();
            if (!_reportedThisTurn && PromptCache.LifetimeOf(writes) is { } lifetime)
            {
                ReportedLifetime = lifetime;
            }
        }
        _calledThisTurn = false;
        _reportedThisTurn = false;
    }

    /// <summary>
    /// A main-agent call of the earlier conversation, read back from its transcript as a tab is restored, ended at
    /// <paramref name="at"/>: a tab reopened within the cache's lifetime shows what's left of it.
    /// </summary>
    public void Replayed(DateTimeOffset at, PromptCacheWrites? writes)
    {
        if (RefreshedAt is { } refreshed && refreshed > at)
        {
            return;
        }
        RefreshedAt = at;
        if (PromptCache.LifetimeOf(writes) is { } lifetime)
        {
            ReportedLifetime = lifetime;
        }
    }

    /// <summary>
    /// The cached conversation won't be read again: it was cleared or compacted, or the next request goes to another
    /// model, whose cache is its own. The lifetime the writes showed still holds for the next ones.
    /// </summary>
    public void Forget()
    {
        RefreshedAt = null;
        _calledThisTurn = false;
        _reportedThisTurn = false;
    }

    /// <summary>As <see cref="Forget"/>, and the lifetime too: the tab moved to another account, whose sign-in may differ.</summary>
    public void Reset()
    {
        Forget();
        ReportedLifetime = null;
    }

    /// <summary>When the cache runs out with <paramref name="lifetime"/>, and what's left of it now; null before the first request.</summary>
    public PromptCacheCountdown? Countdown(TimeSpan lifetime) =>
        RefreshedAt is { } at ? new PromptCacheCountdown(at + lifetime, at + lifetime - time.GetUtcNow()) : null;
}

/// <summary>When a prompt cache expires, and how long that leaves.</summary>
public readonly record struct PromptCacheCountdown(DateTimeOffset ExpiresAt, TimeSpan Remaining)
{
    public bool IsExpired => Remaining <= TimeSpan.Zero;

    /// <summary>In its last three minutes (<see cref="PromptCache.EndingWindow"/>).</summary>
    public bool IsEnding => !IsExpired && Remaining < PromptCache.EndingWindow;

    /// <summary>The whole minutes left, rounded down, so it never promises more time than there is.</summary>
    public int WholeMinutes => IsExpired ? 0 : (int)Remaining.TotalMinutes;
}
