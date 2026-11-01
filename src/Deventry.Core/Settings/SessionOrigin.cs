namespace Deventry.Core.Settings;

/// <summary>
/// An account a session runs under, as far as Deventry can tell (DESIGN.md §4, "Continuing under another account"): one of
/// Deventry's API-key accounts by id, or the Default account, Claude Code's own sign-in, by the email and organization it
/// was signed in with.
/// </summary>
public sealed class SessionOrigin
{
    /// <summary>The API-key account's id; null for the Default account.</summary>
    public string? AccountId { get; set; }

    /// <summary>The API-key account's name when it was recorded; empty for Default.</summary>
    public string Name { get; set; } = "";

    /// <summary>The Default account's sign-in email; empty for an API-key account.</summary>
    public string Email { get; set; } = "";

    /// <summary>The Default account's organization; empty when Claude Code didn't name one.</summary>
    public string Organization { get; set; } = "";

    /// <summary>When the session began, so only the newest are kept (<see cref="KeepNewest"/>).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// The most sessions <see cref="AppState.SessionOrigins"/> keeps: about two months of a busy day's sessions, longer
    /// than Claude Code keeps a transcript to resume by default.
    /// </summary>
    public const int MostKept = 2000;

    /// <summary>The same account, for a session begun at <paramref name="startedAt"/>.</summary>
    public SessionOrigin For(DateTimeOffset startedAt) =>
        new() { AccountId = AccountId, Name = Name, Email = Email, Organization = Organization, StartedAt = startedAt };

    /// <summary>Drops the oldest past <paramref name="most"/>, so the machine's state doesn't grow with every session begun.</summary>
    public static void KeepNewest(Dictionary<string, SessionOrigin> origins, int most = MostKept)
    {
        if (origins.Count <= most)
        {
            return;
        }
        foreach (var id in origins.OrderBy(o => o.Value.StartedAt).Take(origins.Count - most).Select(o => o.Key).ToList())
        {
            origins.Remove(id);
        }
    }

    /// <summary>Whether both are the same account: an id, or an email and organization compared ignoring case.</summary>
    public bool IsSameAs(SessionOrigin other) =>
        string.Equals(AccountId, other.AccountId, StringComparison.Ordinal)
        && string.Equals(Email, other.Email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Organization, other.Organization, StringComparison.OrdinalIgnoreCase);

    /// <summary>A short name for the account that's the same for the same account, to remember a confirmation by.</summary>
    public string Key => AccountId is { } id ? $"key:{id}" : $"default:{Email.ToLowerInvariant()}|{Organization.ToLowerInvariant()}";

    /// <summary>The account in words, for the warning. <paramref name="currentName"/> is an API-key account's name now.</summary>
    public string Describe(string? currentName = null) => AccountId is not null
        ? $"{currentName ?? (Name.Length > 0 ? Name : "an API-key account that has been removed")} (an API key)"
        : Organization.Length > 0 ? $"the Default account, {Email} ({Organization})" : $"the Default account, {Email}";
}
