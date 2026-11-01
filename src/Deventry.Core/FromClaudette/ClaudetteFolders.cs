using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Files;
using Deventry.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// Claudette's data and settings folders, which become Deventry's the first time it starts (DESIGN.md §2, "Renaming to
/// Deventry"): renamed, with the paths saved in them that pointed into them rewritten.
/// </summary>
public static class ClaudetteFolders
{
    /// <summary>The file in the data folder whose lock says Claudette is running (<c>SingleInstance.ClaimFileName</c>).</summary>
    public const string ClaimFileName = "instance.lock";

    /// <summary>
    /// What an update from a Claudette before 0.7.0 waits for, in Claudette's data folder; the only thing Deventry
    /// writes there once the folder is its own (<see cref="ClaudetteReadyFile"/>).
    /// </summary>
    public const string ReadyFileName = "restart-ready";

    /// <summary>Claudette's folders: <c>Claudette</c>, or <c>claudette</c> on Linux.</summary>
    public static AppPaths ForCurrentUser() => AppPaths.Named("Claudette", "claudette");

    /// <summary>
    /// <paramref name="Deventrys"/>, except for a folder Deventry doesn't have yet while Claudette's is still there:
    /// Claudette was running when Deventry started, so <see cref="Move"/> left it, and Deventry uses it where it is.
    /// </summary>
    public static AppPaths WhereTheyAre(AppPaths Deventrys) => WhereTheyAre(Deventrys, ForCurrentUser());

    public static AppPaths WhereTheyAre(AppPaths Deventrys, AppPaths claudettes) =>
        Deventrys with
        {
            DataDirectory = Pick(Deventrys.DataDirectory, claudettes.DataDirectory),
            SettingsDirectory = Pick(Deventrys.SettingsDirectory, claudettes.SettingsDirectory),
        };

    /// <summary>
    /// Renames Claudette's folders to Deventry's, when Deventry has none yet and Claudette isn't running, and rewrites
    /// the paths into them saved in the JSON files at their top (the state, the settings, an update's restart
    /// snapshot). Claudette's folder is left alone while Claudette has it. Once Deventry has its own, a Claudette folder
    /// holding nothing but <see cref="ReadyFileName"/> is removed: an update from an old Claudette left it.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    public static bool Move(AppPaths Deventrys, AppPaths claudettes, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var moved = new List<(string From, string To)>();
        foreach (var (from, to) in Pairs(Deventrys, claudettes))
        {
            if (Directory.Exists(to) || !Directory.Exists(from))
            {
                RemoveLeftover(from, to, logger);
                continue;
            }
            if (IsClaimed(Path.Combine(claudettes.DataDirectory, ClaimFileName)))
            {
                logger.LogInformation("Claudette is running, so its folders stay where they are this time.");
                return false;
            }
            try
            {
                Directory.Move(from, to);
                moved.Add((from, to));
                logger.LogInformation("Moved {From} to {To}.", from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Couldn't move {From} to {To}; it's used where it is this time.", from, to);
            }
        }
        if (moved.Count == 0)
        {
            return false;
        }
        foreach (var folder in moved.Select(m => m.To))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                RewritePaths(file, moved, logger);
            }
        }
        return true;
    }

    /// <summary>Rewrites the paths in <paramref name="file"/> that start with a moved folder to start with where it went.</summary>
    public static void RewritePaths(string file, IReadOnlyList<(string From, string To)> moved, ILogger? logger = null)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not { } root || !Rewrite(root, moved))
            {
                return;
            }
            AtomicFile.WriteAllText(file, root.ToJsonString(JsonFileStore<AppState>.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            (logger ?? NullLogger.Instance).LogWarning(ex, "Couldn't rewrite the paths in {File}.", file);
        }
    }

    private static string Pick(string mine, string theirs) => !Directory.Exists(mine) && Directory.Exists(theirs) ? theirs : mine;

    /// <summary>Each of Claudette's folders and Deventry's for it; one pair on macOS, where data and settings share one.</summary>
    private static IEnumerable<(string From, string To)> Pairs(AppPaths Deventrys, AppPaths claudettes)
    {
        yield return (claudettes.DataDirectory, Deventrys.DataDirectory);
        if (!PathsEqual(claudettes.SettingsDirectory, claudettes.DataDirectory))
        {
            yield return (claudettes.SettingsDirectory, Deventrys.SettingsDirectory);
        }
    }

    /// <summary>Whether a running Claudette holds the lock on <paramref name="claim"/>.</summary>
    private static bool IsClaimed(string claim)
    {
        if (!File.Exists(claim))
        {
            return false;
        }
        try
        {
            using var _ = new FileStream(claim, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void RemoveLeftover(string from, string to, ILogger logger)
    {
        if (!Directory.Exists(to) || !Directory.Exists(from) || PathsEqual(from, to))
        {
            return;
        }
        try
        {
            if (Directory.EnumerateFileSystemEntries(from).All(e => Path.GetFileName(e) == ReadyFileName && File.Exists(e)))
            {
                Directory.Delete(from, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Couldn't remove {From}.", from);
        }
    }

    private static bool Rewrite(JsonNode node, IReadOnlyList<(string From, string To)> moved)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    changed |= RewriteChild(obj[key], value => obj[key] = value, moved);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    changed |= RewriteChild(array[index], value => array[index] = value, moved);
                }
                break;
        }
        return changed;
    }

    private static bool RewriteChild(JsonNode? child, Action<JsonNode> replace, IReadOnlyList<(string From, string To)> moved)
    {
        if (child is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (Rewritten(text, moved) is { } path)
            {
                replace(JsonValue.Create(path));
                return true;
            }
            return false;
        }
        return child is not null && Rewrite(child, moved);
    }

    private static string? Rewritten(string text, IReadOnlyList<(string From, string To)> moved)
    {
        foreach (var (from, to) in moved)
        {
            if (text.Length >= from.Length
                && text.StartsWith(from, Comparison)
                && (text.Length == from.Length || text[from.Length] is '/' or '\\'))
            {
                return to + text[from.Length..];
            }
        }
        return null;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), Comparison);

    private static StringComparison Comparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
