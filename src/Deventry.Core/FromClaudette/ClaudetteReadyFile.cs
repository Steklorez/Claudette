namespace Deventry.Core.FromClaudette;

/// <summary>
/// Where a Claudette before 0.7.0 waits to hear that the version it updated to is up (DESIGN.md §2, "Renaming to
/// Deventry", "What Deventry 1.0.0 keeps for the bridge"). From 0.7.0, Claudette says where with <c>--ready-file</c>;
/// before it, Claudette watched its own data folder's <c>restart-ready</c>, which Deventry has moved. Without the nonce
/// there, an old Mac copy's helper takes Deventry for a failed update and puts Claudette back.
/// </summary>
public static class ClaudetteReadyFile
{
    /// <summary>
    /// Where else to write the nonce, after an update that didn't say where (no <c>--ready-file</c>), or null. A source
    /// build's restart isn't an update, and one with <c>DEVENTRY_HOME</c> or <c>CLAUDETTE_HOME</c> has no other folder.
    /// </summary>
    public static string? AlsoFor(bool isUpdate, string? readyFileOption) =>
        isUpdate && readyFileOption is null && ClaudetteVariables.Get(AppPaths.HomeVariable) is null
            ? Path.Combine(ClaudetteFolders.ForCurrentUser().DataDirectory, ClaudetteFolders.ReadyFileName)
            : null;
}
