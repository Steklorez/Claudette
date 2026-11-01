using Deventry.Core.ProjectTools;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// <c>claudette.json</c> and <c>claudette.local.json</c> in people's projects (DESIGN.md §2, "Renaming to Deventry"):
/// read, and written, in place of <c>deventry.json</c> and <c>deventry.local.json</c> while a project has only those.
/// Deventry never renames them, as they're the project's files.
/// </summary>
public static class ClaudetteProjectFiles
{
    public const string SharedName = "claudette.json";

    public const string LocalName = "claudette.local.json";

    /// <summary>Claudette's file in <paramref name="folder"/>, when it's there and Deventry's, <paramref name="Deventrys"/>, isn't.</summary>
    public static string? InPlaceOf(string folder, ProjectFileScope scope, string Deventrys)
    {
        if (File.Exists(Deventrys))
        {
            return null;
        }
        var old = Path.Combine(folder, scope == ProjectFileScope.Shared ? SharedName : LocalName);
        return File.Exists(old) ? old : null;
    }
}
