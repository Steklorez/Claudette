using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Files;
using Deventry.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// The settings named after Claudette the mascot, which are named after Joulene in Deventry (DESIGN.md §2, "Renaming to
/// Deventry"): read from their old keys while the new ones are missing. Another machine still on Claudette, syncing
/// settings, keeps its own, as the synced file has them under their old keys.
/// </summary>
public static class ClaudetteSettings
{
    /// <summary>Each setting's key in Claudette, and in Deventry, under <c>appearance</c>.</summary>
    public static readonly IReadOnlyList<(string Old, string New)> Renamed =
    [
        ("showClaudette", "showJoulene"),
        ("claudetteSize", "jouleneSize"),
        ("claudetteLiveliness", "jouleneLiveliness"),
        ("claudetteTips", "jouleneTips"),
        ("gamesWithClaudette", "gamesWithJoulene"),
    ];

    /// <summary>
    /// Gives <paramref name="settingsFile"/> the new key of each renamed setting that has only its old one, with its
    /// value, before the settings are read. A file that can't be read or isn't JSON is left to the settings store.
    /// </summary>
    /// <returns>Whether the file changed.</returns>
    public static bool Upgrade(string settingsFile, ILogger? logger = null)
    {
        try
        {
            if (!File.Exists(settingsFile)
                || JsonNode.Parse(File.ReadAllText(settingsFile)) is not JsonObject root
                || root["appearance"] is not JsonObject appearance)
            {
                return false;
            }
            var changed = false;
            foreach (var (old, renamed) in Renamed)
            {
                if (appearance[old] is { } value && !appearance.ContainsKey(renamed))
                {
                    appearance[renamed] = value.DeepClone();
                    changed = true;
                }
            }
            if (changed)
            {
                AtomicFile.WriteAllText(settingsFile, root.ToJsonString(JsonFileStore<AppSettings>.Options));
            }
            return changed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            (logger ?? NullLogger.Instance).LogWarning(ex, "Couldn't carry Claudette's settings over in {File}.", settingsFile);
            return false;
        }
    }
}
