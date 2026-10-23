using System.Runtime.InteropServices;
using Claudette.Core.Updates;

namespace Claudette.App.Services;

/// <summary>
/// Where Claudette sends people for help (DESIGN.md §14, "Version"): the user guide, dotBunny's Discord for questions,
/// and a new GitHub issue with the versions filled in for problems. The foot of the Settings sidebar, the command palette
/// and the macOS Help menu open them in the browser.
/// </summary>
public static class HelpLinks
{
    /// <summary>Claudette's user guide on the web (DESIGN.md §2, "User Guide").</summary>
    public const string GuideUrl = "https://claudette.dotbunny.com/guide/";

    /// <summary>
    /// dotBunny's Discord, through the guide's site, which forwards to the invite (<c>docs/discord.md</c>): a new invite
    /// changes only the site, not Claudette.
    /// </summary>
    public const string DiscordUrl = "https://claudette.dotbunny.com/discord/";

    /// <summary>Claudette's version, Claude Code's, the OS and the runtime: only versions, since it's meant to be posted.</summary>
    public static IReadOnlyList<string> VersionDetails(AppServices services) => services.Build.Details(
        services.InstalledClaudeVersion?.ToString(),
        RuntimeInformation.OSDescription,
        RuntimeInformation.RuntimeIdentifier,
        RuntimeInformation.FrameworkDescription);

    public static Task OpenGuideAsync(AppServices services) => services.Platform.OpenUrlAsync(GuideUrl);

    public static Task OpenDiscordAsync(AppServices services) => services.Platform.OpenUrlAsync(DiscordUrl);

    /// <summary>A new issue on Claudette's GitHub with the versions filled in. Nothing is sent until the user submits it.</summary>
    public static Task ReportIssueAsync(AppServices services) =>
        services.Platform.OpenUrlAsync(AppBuild.NewIssueUrl(ReleaseFeed.DefaultRepository, VersionDetails(services)));
}
