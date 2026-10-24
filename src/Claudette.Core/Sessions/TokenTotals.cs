namespace Claudette.Core.Sessions;

/// <summary>Tokens and estimated cost for one model.</summary>
public sealed class ModelTokenTotals
{
    public long Input { get; set; }

    public long Output { get; set; }

    public long CacheWrite { get; set; }

    public long CacheRead { get; set; }

    /// <summary>Claude Code's client-side estimate at list price; not a bill.</summary>
    public double EstimatedCostUsd { get; set; }

    public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>
/// A tab's running token counts, split by model (DESIGN.md §4, "Token stats per tab"). Saved with the tab.
/// </summary>
public sealed class TokenTotals
{
    public Dictionary<string, ModelTokenTotals> Models { get; set; } = [];

    public int Turns { get; set; }

    public long Total => Models.Values.Sum(m => m.Total);

    public double EstimatedCostUsd => Models.Values.Sum(m => m.EstimatedCostUsd);

    /// <summary>
    /// Adds one turn's usage (<see cref="TurnCompleted.Usage"/>, its share of the result's running totals). A turn that
    /// spent nothing isn't counted.
    /// </summary>
    public void Add(ModelUsageTotals turn)
    {
        if (turn.IsEmpty)
        {
            return;
        }
        Turns++;
        foreach (var (model, usage) in turn.Models)
        {
            if (!Models.TryGetValue(model, out var totals))
            {
                totals = new ModelTokenTotals();
                Models[model] = totals;
            }
            totals.Input += usage.Input;
            totals.Output += usage.Output;
            totals.CacheWrite += usage.CacheWrite;
            totals.CacheRead += usage.CacheRead;
            totals.EstimatedCostUsd += usage.CostUsd;
        }
    }

    /// <summary>Short form for the composer bar, for example <c>1.2M tok</c>.</summary>
    public static string Short(long tokens) => tokens switch
    {
        // From 999,950, which would round to "1000k".
        >= 999_950 => $"{tokens / 1_000_000.0:0.#}M tok",
        >= 1_000 => $"{tokens / 1_000.0:0.#}k tok",
        _ => $"{tokens} tok",
    };
}
