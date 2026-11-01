namespace Deventry.Core.FromClaudette;

/// <summary>
/// Claudette's names for Deventry's environment variables, <c>CLAUDETTE_HOME</c> for <c>DEVENTRY_HOME</c> and so on
/// (DESIGN.md §2, "Renaming to Deventry"): read where people set them for Claudette, and given beside Deventry's to
/// what people's own scripts run in.
/// </summary>
/// <remarks>
/// Everything in this folder carries over what Claudette left, and goes once the release that drops it says so; each
/// use of it outside the folder says <c>FromClaudette</c>.
/// </remarks>
public static class ClaudetteVariables
{
    /// <summary>What Claudette's variables start with.</summary>
    public const string Prefix = "CLAUDETTE_";

    private const string DeventryPrefix = "DEVENTRY_";

    /// <summary>Claudette's name for <paramref name="name"/>: <c>CLAUDETTE_HOME</c> for <c>DEVENTRY_HOME</c>.</summary>
    public static string OldName(string name) =>
        name.StartsWith(DeventryPrefix, StringComparison.Ordinal) ? Prefix + name[DeventryPrefix.Length..] : name;

    /// <summary><paramref name="name"/>'s value, or the value of Claudette's name for it when that's all there is.</summary>
    public static string? Get(string name) => Get(name, Environment.GetEnvironmentVariable);

    public static string? Get(string name, Func<string, string?> read) =>
        read(name) is { Length: > 0 } value ? value : read(OldName(name)) is { Length: > 0 } old ? old : null;

    /// <summary>Gives each of <paramref name="names"/> in <paramref name="environment"/> its Claudette name too, with the same value.</summary>
    public static void AddTo<T>(IDictionary<string, T> environment, params string[] names)
    {
        foreach (var name in names)
        {
            if (environment.TryGetValue(name, out var value))
            {
                environment[OldName(name)] = value;
            }
        }
    }
}
