using Deventry.Core.Credentials;
using Deventry.Core.FromClaudette;
using Microsoft.Extensions.Logging;

namespace Deventry.Platform.FromClaudette;

/// <summary>
/// The names Claudette saved its secrets under in each OS's credential store (DESIGN.md §2, "Renaming to Deventry"),
/// for <see cref="CarriedOverCredentials"/>. Like everything in Core's and Platform's <c>FromClaudette</c> folders, it
/// goes once a release drops what Deventry carries over from Claudette.
/// </summary>
public static class ClaudetteCredentials
{
    /// <summary>Credential Manager's <c>Claudette/&lt;key&gt;</c>.</summary>
    public const string WindowsPrefix = "Claudette";

    /// <summary>The Keychain's service.</summary>
    public const string MacService = "Claudette";

    /// <summary><c>secret-tool</c>'s <c>application</c> attribute.</summary>
    public const string LinuxApplication = "claudette";

    public static ICredentialStore CarriedOver(ICredentialStore Deventrys, ICredentialStore claudettes, ILoggerFactory? loggerFactory = null) =>
        new CarriedOverCredentials(Deventrys, claudettes, loggerFactory?.CreateLogger<CarriedOverCredentials>());
}
