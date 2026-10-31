using Deventry.Core.Credentials;
using Deventry.Core.Processes;
using Deventry.Platform.Credentials.Linux;
using Deventry.Platform.Credentials.Mac;
using Deventry.Platform.Credentials.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Platform.Credentials;

public static class CredentialStores
{
    /// <summary>
    /// The credential store for the OS Claudette is running on: Windows Credential Manager, the macOS Keychain, or the
    /// Secret Service through <c>secret-tool</c> on Linux. Anything that fails to set up gives an unavailable store.
    /// </summary>
    public static ICredentialStore CreateForCurrentOS(IProcessLauncher launcher, TimeProvider timeProvider, ILoggerFactory? loggerFactory = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // FromClaudette: with what Claudette saved under its own name carried over.
                return FromClaudette.ClaudetteCredentials.CarriedOver(new WindowsCredentialStore(), new WindowsCredentialStore(FromClaudette.ClaudetteCredentials.WindowsPrefix), loggerFactory);
            }
            if (OperatingSystem.IsMacOS())
            {
                return FromClaudette.ClaudetteCredentials.CarriedOver(new MacKeychain(), new MacKeychain(FromClaudette.ClaudetteCredentials.MacService), loggerFactory);
            }
            if (OperatingSystem.IsLinux())
            {
                return SecretToolCredentialStore.TryCreate(launcher, timeProvider) is { } store
                    ? FromClaudette.ClaudetteCredentials.CarriedOver(store, new SecretToolCredentialStore(store.SecretTool, launcher, timeProvider, FromClaudette.ClaudetteCredentials.LinuxApplication), loggerFactory)
                    : new UnavailableCredentialStore("the Secret Service", SecretToolCredentialStore.MissingReason);
            }
        }
        catch (Exception ex)
        {
            (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(typeof(CredentialStores)).LogWarning(ex, "The OS credential store isn't available.");
        }
        return new UnavailableCredentialStore();
    }
}
