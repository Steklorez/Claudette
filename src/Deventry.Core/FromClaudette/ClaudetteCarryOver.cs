using Microsoft.Extensions.Logging;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// What Deventry takes over from Claudette as it starts, before anything reads its folders (DESIGN.md §2, "Renaming to
/// Deventry", "What moves the first time Deventry starts"). Each launch looks again, so what Claudette was holding the
/// first time moves once it lets go.
/// </summary>
public static class ClaudetteCarryOver
{
    public static void Run(ILogger? logger = null)
    {
        if (ClaudetteVariables.Get(AppPaths.HomeVariable) is null)
        {
            ClaudetteFolders.Move(AppPaths.Default(), ClaudetteFolders.ForCurrentUser(), logger);
        }
        ClaudetteSettings.Upgrade(AppPaths.ForCurrentUser().SettingsFile, logger);
    }
}
