namespace Deventry.Core.Accounts;

/// <summary>
/// Anthropic's list prices for fresh input, by model (DESIGN.md §18, "Prices"): what prices a call before its model has had
/// a result to go by, and what moving a conversation to another account costs (<see cref="SwitchCost"/>). Every other kind
/// of token follows from the weights every Claude model shares (<see cref="Sessions.CallTokens.Weighted"/>). From
/// Anthropic's price list of 2026-09-25; a model not here gets no estimate rather than a wrong one.
/// </summary>
public static class ListPrices
{
    /// <summary>US dollars per million tokens of fresh input, by model id prefix, longest prefixes first.</summary>
    private static readonly (string Prefix, double PerMillion)[] InputPrices =
    [
        ("claude-fable-5", 10), ("claude-mythos-5", 10),
        ("claude-opus-5-5", 4), ("claude-opus-5", 5), ("claude-opus-4", 5),
        ("claude-sonnet-5", 2), ("claude-sonnet-4-6", 3),
        ("claude-haiku-4-5", 1),
    ];

    /// <summary>
    /// A token of fresh input on <paramref name="model"/>, in US dollars at list price: a model id such as
    /// <c>claude-opus-5-5</c>, with or without a <c>[1m]</c> suffix, or a cloud provider's, such as Bedrock's
    /// <c>us.anthropic.claude-opus-4-8</c> or the Agent Platform's <c>claude-haiku-4-5@20251001</c>. Null for a model
    /// without a known price.
    /// </summary>
    public static double? Input(string? model)
    {
        if (model is null)
        {
            return null;
        }
        // A cloud provider's id names the model after a region and the publisher: us.anthropic.claude-…
        var at = model.IndexOf("claude-", StringComparison.OrdinalIgnoreCase);
        if (at > 0 && model[..at].EndsWith("anthropic.", StringComparison.OrdinalIgnoreCase))
        {
            model = model[at..];
        }
        foreach (var (prefix, perMillion) in InputPrices)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return perMillion / 1_000_000;
            }
        }
        return null;
    }
}
