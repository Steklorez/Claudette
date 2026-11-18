using System.Globalization;
using Deventry.Core.Sessions;

namespace Deventry.Core.Accounts;

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
    /// The re-read's cost in US dollars at list price for <paramref name="tokens"/> of conversation on
    /// <paramref name="model"/> (as <see cref="ListPrices.Input"/> names it), or null for a model without a known price.
    /// </summary>
    public static double? Estimate(string? model, long tokens) =>
        tokens > 0 && ListPrices.Input(model) is { } price ? tokens * price * CacheWriteMultiplier : null;

    /// <summary>What the confirmation says the switch costs: the tokens, and the estimate when it's billed by the token.</summary>
    /// <param name="accountName">The account it moves to.</param>
    /// <param name="billing">How the account pays: from a plan's limits, or by the token to an API key or a cloud provider.</param>
    /// <param name="provider">The cloud provider's name, for <see cref="ClaudeBilling.CloudProvider"/>.</param>
    public static string Describe(string accountName, long tokens, string? model, ClaudeBilling billing, string? provider = null)
    {
        var read = tokens > 0
            ? $"Claude reads the conversation's {tokens.ToString("N0", CultureInfo.CurrentCulture)} tokens again under {accountName}, without the prompt cache, once"
            : $"Claude reads the conversation again under {accountName}, without the prompt cache, once";
        var estimate = Estimate(model, tokens);
        return billing switch
        {
            ClaudeBilling.Subscription => $"{read}, which counts toward its plan's limits.",
            ClaudeBilling.CloudProvider when estimate is { } dollars => $"{read}: about {Formats.Dollars(dollars)} at Anthropic's list price, billed through {provider ?? "its cloud provider"}.",
            ClaudeBilling.CloudProvider => $"{read}, billed through {provider ?? "its cloud provider"}.",
            _ when estimate is { } dollars => $"{read}: about {Formats.Dollars(dollars)} at list price.",
            _ => $"{read}, billed to its API key.",
        };
    }
}
