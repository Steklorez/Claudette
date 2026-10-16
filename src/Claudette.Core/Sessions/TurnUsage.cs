using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Sessions;

/// <summary>One model's tokens and estimated cost.</summary>
/// <param name="CostUsd">Claude Code's client-side estimate at list price; not a bill.</param>
public sealed record UsageCounts(long Input, long Output, long CacheWrite, long CacheRead, double CostUsd)
{
    /// <summary>Below this, a difference in cost is rounding, not spend.</summary>
    private const double CostTolerance = 1e-9;

    public long Total => Input + Output + CacheWrite + CacheRead;

    public bool IsZero => Total == 0 && CostUsd <= CostTolerance;

    /// <summary>What these counts added to <paramref name="earlier"/>, field by field and never below zero.</summary>
    public UsageCounts Minus(UsageCounts earlier) => new(
        Math.Max(0, Input - earlier.Input),
        Math.Max(0, Output - earlier.Output),
        Math.Max(0, CacheWrite - earlier.CacheWrite),
        Math.Max(0, CacheRead - earlier.CacheRead),
        CostUsd - earlier.CostUsd > CostTolerance ? CostUsd - earlier.CostUsd : 0);

    /// <summary>Every field is at least <paramref name="earlier"/>'s: these counts could have grown from those.</summary>
    public bool Covers(UsageCounts earlier) =>
        Input >= earlier.Input && Output >= earlier.Output && CacheWrite >= earlier.CacheWrite && CacheRead >= earlier.CacheRead
        && CostUsd >= earlier.CostUsd - CostTolerance;
}

/// <summary>
/// Tokens and estimated cost by model (DESIGN.md §6, "Usage history"): the running totals a <c>result</c> carries in
/// <c>modelUsage</c>, or one turn's share of them (<see cref="ProcessUsage"/>).
/// </summary>
public sealed class ModelUsageTotals
{
    public static readonly ModelUsageTotals Empty = new(new Dictionary<string, UsageCounts>());

    public ModelUsageTotals(IReadOnlyDictionary<string, UsageCounts> models) => Models = models;

    /// <summary>Only models with something counted.</summary>
    public IReadOnlyDictionary<string, UsageCounts> Models { get; }

    public bool IsEmpty => Models.Count == 0;

    public long Total => Models.Values.Sum(m => m.Total);

    public double CostUsd => Models.Values.Sum(m => m.CostUsd);

    /// <summary>
    /// Reads a <c>modelUsage</c> object: a result's, or the one Claude Code saves in a transcript's <c>cost-state</c>
    /// entry. A model with nothing counted is left out; a field that's missing or isn't a number reads as zero.
    /// </summary>
    public static ModelUsageTotals From(JsonObject? modelUsage)
    {
        if (modelUsage is null)
        {
            return Empty;
        }
        var models = new Dictionary<string, UsageCounts>(StringComparer.Ordinal);
        foreach (var (model, node) in modelUsage)
        {
            if (node is not JsonObject usage)
            {
                continue;
            }
            var counts = new UsageCounts(
                usage["inputTokens"].AsWholeNumber() ?? 0,
                usage["outputTokens"].AsWholeNumber() ?? 0,
                usage["cacheCreationInputTokens"].AsWholeNumber() ?? 0,
                usage["cacheReadInputTokens"].AsWholeNumber() ?? 0,
                usage.GetDouble("costUSD") ?? 0);
            if (!counts.IsZero)
            {
                models[model] = counts;
            }
        }
        return models.Count == 0 ? Empty : new ModelUsageTotals(models);
    }

    /// <summary>What these totals added to <paramref name="earlier"/>: the models that grew, and by how much.</summary>
    public ModelUsageTotals Minus(ModelUsageTotals earlier)
    {
        var models = new Dictionary<string, UsageCounts>(StringComparer.Ordinal);
        foreach (var (model, counts) in Models)
        {
            var added = earlier.Models.TryGetValue(model, out var before) ? counts.Minus(before) : counts;
            if (!added.IsZero)
            {
                models[model] = added;
            }
        }
        return models.Count == 0 ? Empty : new ModelUsageTotals(models);
    }

    /// <summary>
    /// These totals could have grown from <paramref name="earlier"/>: they have every model it has, each with at least
    /// as much.
    /// </summary>
    public bool Covers(ModelUsageTotals earlier) =>
        earlier.Models.All(e => Models.TryGetValue(e.Key, out var counts) && counts.Covers(e.Value));
}

/// <summary>
/// Turns the running totals of one <c>claude</c> process into each turn's usage (DESIGN.md §6, "Usage history"). A
/// <c>result</c>'s <c>modelUsage</c> counts everything the process has spent since it started, or since the last
/// <c>/clear</c>, and for a resumed or forked session also what Claude Code restored from the transcript; each turn's
/// share is what it added to the result before. Not thread-safe: the session's read loop is its only caller.
/// </summary>
public sealed class ProcessUsage
{
    private ModelUsageTotals _previous = ModelUsageTotals.Empty;
    private IReadOnlyList<ModelUsageTotals> _restored;

    /// <param name="restored">
    /// The totals a resumed or forked session's transcript saved (<c>cost-state</c> entries, oldest first), one of which
    /// Claude Code starts the process from: the latest one the first result could have grown from is taken.
    /// </param>
    public ProcessUsage(IReadOnlyList<ModelUsageTotals>? restored = null) => _restored = restored ?? [];

    /// <summary>
    /// This turn's usage, from its result's running totals. Empty for a result with none, such as <c>/clear</c>'s own, an
    /// interrupt before anything was counted, or a crash's zeroed result, which leave the totals where they were.
    /// </summary>
    public ModelUsageTotals Turn(ResultMessage result)
    {
        var current = ModelUsageTotals.From(result.ModelUsage);
        if (current.IsEmpty)
        {
            return ModelUsageTotals.Empty;
        }
        if (_restored.Count > 0)
        {
            _previous = _restored.LastOrDefault(current.Covers) ?? ModelUsageTotals.Empty;
            _restored = [];
        }
        var turn = current.Minus(_previous);
        _previous = current;
        return turn;
    }

    /// <summary>The conversation was cleared: Claude Code starts its running totals again.</summary>
    public void Reset()
    {
        _previous = ModelUsageTotals.Empty;
        _restored = [];
    }
}
