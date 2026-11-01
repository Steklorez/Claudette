using Deventry.Core.Credentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// The OS credential store under Deventry's name, with what Claudette saved under its own carried over (DESIGN.md §2,
/// "Renaming to Deventry"): a secret found only under Claudette's name (Credential Manager's <c>Claudette/…</c>, the
/// Keychain's <c>Claudette</c> service, <c>secret-tool</c>'s <c>claudette</c>) is saved under Deventry's and deleted.
/// The bundle identifier stays, so the Keychain doesn't ask.
/// </summary>
/// <param name="Deventrys">The store under Deventry's name.</param>
/// <param name="claudettes">The same store under Claudette's.</param>
public sealed class CarriedOverCredentials(ICredentialStore Deventrys, ICredentialStore claudettes, ILogger? logger = null) : ICredentialStore
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public string Name => Deventrys.Name;

    public bool IsAvailable => Deventrys.IsAvailable;

    public string? UnavailableReason => Deventrys.UnavailableReason;

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (await Deventrys.ReadAsync(key, cancellationToken).ConfigureAwait(false) is { } secret)
        {
            return secret;
        }
        if (await claudettes.ReadAsync(key, cancellationToken).ConfigureAwait(false) is not { } old)
        {
            return null;
        }
        try
        {
            await Deventrys.WriteAsync(key, Label(key), old, cancellationToken).ConfigureAwait(false);
            await claudettes.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (CredentialStoreException ex)
        {
            // Still there under Claudette's name, to try again next time.
            _logger.LogWarning(ex, "Couldn't carry {Key} over from Claudette's name.", key);
        }
        return old;
    }

    public async Task WriteAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
    {
        await Deventrys.WriteAsync(key, label, secret, cancellationToken).ConfigureAwait(false);
        await DeleteOldAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var deleted = await Deventrys.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        return await DeleteOldAsync(key, cancellationToken).ConfigureAwait(false) || deleted;
    }

    /// <summary>
    /// What the OS shows for a secret carried over, worked out from its key, as Claudette's own label can't be read
    /// back: "Deventry: Perforce matt @ ssl:perforce:1666", "Deventry: Claude API key". Saving it again in Settings
    /// gives it its full label.
    /// </summary>
    public static string Label(string key)
    {
        var parts = key.Split('/');
        return parts switch
        {
            ["perforce", .., var user] when parts.Length >= 3 => $"Deventry: Perforce {user} @ {string.Join('/', parts[1..^1])}",
            ["claude-api-key", _] => "Deventry: Claude API key",
            _ => $"Deventry: {key}",
        };
    }

    private async Task<bool> DeleteOldAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await claudettes.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (CredentialStoreException ex)
        {
            _logger.LogDebug(ex, "Couldn't delete {Key} under Claudette's name.", key);
            return false;
        }
    }
}
