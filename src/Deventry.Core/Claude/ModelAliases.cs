using System.Globalization;
using Deventry.Core.Sessions;

namespace Deventry.Core.Claude;

/// <summary>
/// Claude Code's documented model aliases for each family (<c>fable</c>, <c>opus</c>, <c>sonnet</c>, <c>haiku</c>), which
/// always run the family's newest model (DESIGN.md §5, "Model &amp; effort").
/// </summary>
/// <remarks>
/// Signed in with a token from <c>CLAUDE_CODE_OAUTH_TOKEN</c> (<c>claude setup-token</c>), Claude Code can't see the
/// plan, and its <c>initialize</c> reply can offer a family only as an older pinned model (<c>claude-fable-5-1[1m]</c>)
/// while the alias already runs a newer one. <see cref="WithFamilyAliases"/> adds the alias to the list then, so the
/// picker offers the newest model. No version number is written here: the alias is Claude Code's to resolve.
/// </remarks>
public static class ModelAliases
{
    /// <summary>The documented family aliases, newest family first as Claude Code lists them.</summary>
    public static IReadOnlyList<string> Families { get; } = ["fable", "opus", "sonnet", "haiku"];

    /// <summary>Claude Code's own entry for its default model, which isn't a family's.</summary>
    private const string DefaultValue = "default";

    /// <summary>
    /// The family a model id or alias belongs to: <c>fable</c> for <c>fable</c>, <c>fable[1m]</c>, <c>claude-fable-5-1[1m]</c>
    /// or a dated <c>claude-haiku-4-5-20251001</c>; null for anything else, <c>default</c> among them.
    /// </summary>
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

    /// <summary>The id is a family's alias (<c>fable</c>, <c>fable[1m]</c>), not a pinned model.</summary>
    public static bool IsAlias(string? id) => id is not null && Families.Contains(Bare(id), StringComparer.Ordinal);

    /// <summary>
    /// A name for a model id Claude Code reports, from the id alone: "Fable 5.2" for <c>claude-fable-5-2</c>, "Haiku 4.5" for
    /// <c>claude-haiku-4-5-20251001</c>, with " (1M context)" for a <c>[1m]</c> id. Null when the id doesn't say its family
    /// and version.
    /// </summary>
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
            // The older order, claude-3-5-sonnet: the version comes before the family.
            version = [.. segments.Take(at).Reverse().TakeWhile(IsVersionPart).Reverse()];
        }
        if (version.Count == 0)
        {
            return null;
        }
        var oneMillion = id!.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase) ? " (1M context)" : "";
        return $"{FamilyName(family)} {string.Join('.', version)}{oneMillion}";
    }

    /// <summary>"Fable" for <c>fable</c>.</summary>
    public static string FamilyName(string family) =>
        family.Length == 0 ? family : char.ToUpperInvariant(family[0]) + family[1..];

    /// <summary>
    /// Claude Code's models with each family's alias added where the family is offered only as pinned models: an entry
    /// whose value is the alias, named "Fable (newest)", right before the family's first pinned entry, with that entry's
    /// effort levels and Auto mode. The list is returned as it is when every family on it has its alias (a normal
    /// sign-in), and Claude Code's <c>default</c> entry is never read as a family's.
    /// </summary>
    public static IReadOnlyList<ModelInfo> WithFamilyAliases(IReadOnlyList<ModelInfo> models)
    {
        var offered = models.Where(m => m.Value != DefaultValue).ToList();
        var withAlias = offered.Where(m => IsAlias(m.Value)).Select(m => FamilyOf(m.Value)).ToHashSet(StringComparer.Ordinal);
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

    /// <summary>The family's alias, standing in the picker before <paramref name="pinned"/>, whose capabilities it shares.</summary>
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

    /// <summary>Without a <c>[1m]</c> suffix, in lower case.</summary>
    private static string Bare(string id)
    {
        var bracket = id.IndexOf('[', StringComparison.Ordinal);
        return (bracket >= 0 ? id[..bracket] : id).Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> Segments(string bare) =>
        bare.Split(['-', '.', '/', ':', '@', '_'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A version number's part, as in <c>5-2</c>; a date (<c>20251001</c>) isn't one.</summary>
    private static bool IsVersionPart(string segment) =>
        segment.Length is > 0 and <= 2 && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
