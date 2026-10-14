using Claudette.Core.Accounts;
using Claudette.Core.Credentials;
using Claudette.Core.Processes;
using Claudette.Platform.Credentials;

namespace Claudette.App.Services;

/// <summary>
/// Claudette started by Claude Code as an API-key account's <c>apiKeyHelper</c> (DESIGN.md §4, "Accounts"): it prints
/// the account's key from the OS credential store, and nothing else, and exits. No window, and no hand-off to a running
/// Claudette: Claude Code reads the key from this process's output.
/// </summary>
internal static class ApiKeyHelperLaunch
{
    /// <returns>The exit code: 0 with the key printed, 1 without it.</returns>
    public static int Run(string accountId, ICredentialStore? store = null, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        try
        {
            store ??= CredentialStores.CreateForCurrentOS(new ProcessLauncher(), TimeProvider.System);
            var key = store.ReadAsync(ApiKeyHelper.CredentialKey(accountId)).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(key))
            {
                // Plain ASCII: the console's code page may not have an arrow.
                error.WriteLine($"Claudette has no API key for this account in {store.Name}. Enter it again in Claudette's settings, under Accounts.");
                return 1;
            }
            output.Write(key);
            output.Flush();
            return 0;
        }
        catch (Exception ex) when (ex is CredentialStoreException or IOException or InvalidOperationException)
        {
            error.WriteLine($"Claudette couldn't read the API key: {ex.Message}");
            return 1;
        }
    }
}
