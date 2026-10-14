using System.Globalization;

namespace Claudette.Core.Accounts;

/// <summary>
/// What moving a conversation to another account costs (DESIGN.md §4, "Accounts"): the prompt cache is never shared
/// between organizations, so the next turn reads the whole conversation again without it, once, and writes it to the new
/// account's cache.
/// </summary>
public static class SwitchCost
{
    /// <summary>A cache write costs this much more than plain input (the five-minute cache's rate).</summary>
    private const double CacheWriteMultiplier = 1.25;

    /// <summary>
    /// Input prices in US dollars per million tokens, at list price, by model id prefix, longest prefixes first. From
    /// Anthropic's price list of 2026-09-25; a model not here gets no estimate rather than a wrong one.
    /// </summary>
    private static readonly (string Prefix, double PerMillion)[] InputPrices =
    [
        ("claude-fable-5", 10), ("claude-mythos-5", 10),
        ("claude-opus-5-5", 4), ("claude-opus-5", 5), ("claude-opus-4", 5),
        ("claude-sonnet-5", 2), ("claude-sonnet-4-6", 3),
        ("claude-haiku-4-5", 1),
    ];

    /// <summary>
    /// The re-read's cost in US dollars at list price for <paramref name="tokens"/> of conversation on
    /// <paramref name="model"/> (a model id such as <c>claude-opus-5-5</c>, with or without a <c>[1m]</c> suffix), or null
    /// for a model without a known price.
    /// </summary>
    public static double? Estimate(string? model, long tokens)
    {
        if (model is null || tokens <= 0)
        {
            return null;
        }
        foreach (var (prefix, perMillion) in InputPrices)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return tokens * perMillion * CacheWriteMultiplier / 1_000_000;
            }
        }
        return null;
    }

    /// <summary>What the confirmation says the switch costs: the tokens, and the estimate when it's billed by the token.</summary>
    /// <param name="accountName">The account it moves to.</param>
    /// <param name="billedByToken">The account pays per token (an API key), rather than from a plan's limits.</param>
    public static string Describe(string accountName, long tokens, string? model, bool billedByToken)
    {
        var read = tokens > 0
            ? $"Claude reads the conversation's {tokens.ToString("N0", CultureInfo.CurrentCulture)} tokens again under {accountName}, without the prompt cache, once"
            : $"Claude reads the conversation again under {accountName}, without the prompt cache, once";
        return billedByToken && Estimate(model, tokens) is { } dollars
            ? $"{read}: about {Formats.Dollars(dollars)} at list price."
            : billedByToken ? $"{read}, billed to its API key." : $"{read}, which counts toward its plan's limits.";
    }
}
