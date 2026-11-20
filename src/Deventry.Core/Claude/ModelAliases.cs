using System.Globalization;
using Deventry.Core.Sessions;

namespace Deventry.Core.Claude;

/// <summary>Claude Code's family aliases (<c>fable</c>, <c>opus</c>, <c>sonnet</c>, <c>haiku</c>), which run the family's newest model.</summary>
public static class ModelAliases
{
    public static IReadOnlyList<string> Families { get; } = ["fable", "opus", "sonnet", "haiku"];

    private const string DefaultValue = "default";

    /// <summary><c>fable</c> for <c>fable</c>, <c>fable[1m]</c> or <c>claude-fable-5-1[1m]</c>; null otherwise.</summary>
    public static string? FamilyOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        var bare = Bare(id);
        if (Families.Contains(bare, StringComparer.Ordinal))
        {
            return bare;
        }
        if (!bare.Contains("claude", StringComparison.Ordinal))
        {
            return null;
        }
        return Segments(bare).FirstOrDefault(s => Families.Contains(s, StringComparer.Ordinal));
    }

    public static bool IsAlias(string? id) => id is not null && Families.Contains(Bare(id), StringComparer.Ordinal);

    /// <summary>"Fable 5.2" for <c>claude-fable-5-2</c>; null when the id doesn't say its family and version.</summary>
    public static string? NameOf(string? id)
    {
        if (FamilyOf(id) is not { } family || IsAlias(id))
        {
            return null;
        }
        var segments = Segments(Bare(id!)).ToList();
        var at = segments.IndexOf(family);
        var version = segments.Skip(at + 1).TakeWhile(IsVersionPart).ToList();
        if (version.Count == 0)
        {
            // claude-3-5-sonnet: the older order.
            version = [.. segments.Take(at).Reverse().TakeWhile(IsVersionPart).Reverse()];
        }
        if (version.Count == 0)
        {
            return null;
        }
        var oneMillion = id!.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase) ? " (1M context)" : "";
        return $"{FamilyName(family)} {string.Join('.', version)}{oneMillion}";
    }

    public static string FamilyName(string family) =>
        family.Length == 0 ? family : char.ToUpperInvariant(family[0]) + family[1..];

    /// <summary>
    /// Adds a family's alias ("Fable (newest)") before its first entry when Claude Code offered the family only as pinned
    /// models, as it does when signed in with <c>CLAUDE_CODE_OAUTH_TOKEN</c> and can't see the plan.
    /// </summary>
    public static IReadOnlyList<ModelInfo> WithFamilyAliases(IReadOnlyList<ModelInfo> models)
    {
        var withAlias = models.Where(m => m.Value != DefaultValue && IsAlias(m.Value))
            .Select(m => FamilyOf(m.Value)).ToHashSet(StringComparer.Ordinal);
        var added = new HashSet<string>(StringComparer.Ordinal);
        List<ModelInfo>? result = null;
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            if (model.Value != DefaultValue
                && (FamilyOf(model.Value) ?? FamilyOf(model.ResolvedModel)) is { } family
                && !withAlias.Contains(family)
                && added.Add(family))
            {
                result ??= [.. models.Take(i)];
                result.Add(NewestOf(family, model));
            }
            result?.Add(model);
        }
        return result is null ? models : result;
    }

    private static ModelInfo NewestOf(string family, ModelInfo pinned) => new(
        family,
        ResolvedModel: null,
        $"{FamilyName(family)} (newest)",
        $"Claude Code's newest {FamilyName(family)} model",
        pinned.SupportsEffort,
        pinned.SupportedEffortLevels)
    {
        SupportsAutoMode = pinned.SupportsAutoMode,
        IsNewestAlias = true,
    };

    private static string Bare(string id)
    {
        var bracket = id.IndexOf('[', StringComparison.Ordinal);
        return (bracket >= 0 ? id[..bracket] : id).Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> Segments(string bare) =>
        bare.Split(['-', '.', '/', ':', '@', '_'], StringSplitOptions.RemoveEmptyEntries);

    // A date (20251001) isn't a version part.
    private static bool IsVersionPart(string segment) =>
        segment.Length is > 0 and <= 2 && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
