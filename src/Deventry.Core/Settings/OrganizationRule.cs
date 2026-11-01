using System.Globalization;

namespace Deventry.Core.Settings;

/// <summary>
/// Something an account's organization has turned off, which Deventry accepts and stops trying (DESIGN.md §18, "Remote
/// Control"). Kept in <see cref="AppState.OrganizationRules"/>, so it's per machine, and removed from Settings → Accounts
/// when the organization changes its mind.
/// </summary>
public sealed class OrganizationRule
{
    /// <summary>The feature Remote Control, the Claude app's connection to a tab (<see cref="Feature"/>).</summary>
    public const string RemoteControl = "remote-control";

    /// <summary>Which feature is off: <see cref="RemoteControl"/> today; a later feature adds its own name.</summary>
    public string Feature { get; set; } = "";

    /// <summary>The signed-in account's email the rule is for; compared ignoring case.</summary>
    public string Email { get; set; } = "";

    /// <summary>The account's organization, as Claude Code named it; empty when it didn't. Compared ignoring case.</summary>
    public string Organization { get; set; } = "";

    /// <summary>What Claude Code said when it turned the feature down, in its words.</summary>
    public string Reason { get; set; } = "";

    /// <summary>When Deventry first heard it.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Whether the rule is about this feature and this account.</summary>
    public bool Matches(string feature, string? email, string? organization) =>
        email is { Length: > 0 }
        && string.Equals(Feature, feature, StringComparison.Ordinal)
        && string.Equals(Email, email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Organization, organization ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>When the rule was added, in the user's own time zone and language, for what Settings and the tabs say.</summary>
    public string Since => AddedAt.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>
    /// What a tab's switch says as its tip: Claude Code's reason, then that it's the organization's rule for the account,
    /// since when, and where to remove it.
    /// </summary>
    public string Describe()
    {
        var reason = Reason.Trim();
        if (reason.Length > 0 && !".!?".Contains(reason[^1]))
        {
            reason += ".";
        }
        return $"{reason} This is your organization's rule for {Email}, kept since {Since}. Remove it in Settings → Accounts to try again.".TrimStart();
    }

    /// <summary>The feature in words, for Settings.</summary>
    public static string FeatureName(string feature) => feature == RemoteControl ? "Remote Control (the Claude app)" : feature;
}
