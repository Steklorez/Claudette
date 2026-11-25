namespace Deventry.Core.Accounts;

/// <summary>Where an account's one secret is kept in the OS credential store, whatever its kind (DESIGN.md §4, "Accounts").</summary>
public static class AccountSecrets
{
    /// <summary>
    /// The credential's key: an API key's, or a cloud provider's key, alike, so removing the account removes it, whatever
    /// it was.
    /// </summary>
    public static string CredentialKey(string accountId) => ApiKeyHelper.CredentialKey(accountId);

    /// <summary>What the OS shows for it.</summary>
    public static string CredentialLabel(ClaudeAccount account) => account.Provider is { } provider
        ? $"Deventry: {provider.KeyName ?? "key"} for {provider.Name} ({account.Name})"
        : account.Server is { } server
            ? $"Deventry: API key for {server.ServerName} ({account.Name})"
            : ApiKeyHelper.CredentialLabel(account.Name);
}
