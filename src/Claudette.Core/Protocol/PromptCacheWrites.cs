using System.Text.Json.Nodes;

namespace Claudette.Core.Protocol;

/// <summary>
/// The <c>cache_creation</c> breakdown of a <c>usage</c> object (DESIGN.md §6, "Prompt cache"): how many input tokens a
/// call, or a turn, wrote to the prompt cache at each lifetime. A <c>result</c>'s usage has it; an <c>assistant</c>
/// message's may, as the Messages API sends it.
/// </summary>
/// <param name="FiveMinuteTokens"><c>ephemeral_5m_input_tokens</c>.</param>
/// <param name="OneHourTokens"><c>ephemeral_1h_input_tokens</c>.</param>
public sealed record PromptCacheWrites(long FiveMinuteTokens, long OneHourTokens)
{
    /// <summary>Anything was written, at either lifetime.</summary>
    public bool WroteAny => FiveMinuteTokens > 0 || OneHourTokens > 0;

    /// <summary>
    /// The <c>cache_creation</c> of <paramref name="usage"/>, or null when it has none. A count that's missing or isn't a
    /// number reads as 0.
    /// </summary>
    public static PromptCacheWrites? From(JsonObject? usage) =>
        usage?.GetObject("cache_creation") is { } creation
            ? new PromptCacheWrites(
                Math.Max(0, creation["ephemeral_5m_input_tokens"].AsWholeNumber() ?? 0),
                Math.Max(0, creation["ephemeral_1h_input_tokens"].AsWholeNumber() ?? 0))
            : null;
}
