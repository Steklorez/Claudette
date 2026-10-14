namespace Claudette.Core.Accounts;

/// <summary>
/// An account a tab can run Claude Code under besides the Default one, Claude Code's own sign-in (DESIGN.md §4,
/// "Accounts"). Kept per machine in the app state; its API key is only in the OS credential store.
/// </summary>
public sealed class ClaudeAccount
{
    /// <summary>Letters and digits only, so it can go in the key helper's command (<see cref="ApiKeyHelper.Command"/>).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>
    /// How the account signs in, as a word (<see cref="AccountKinds"/>): a kind a later Claudette adds reads as one this
    /// one doesn't know, rather than failing the whole file.
    /// </summary>
    public string Kind { get; set; } = AccountKinds.ApiKey;

    /// <summary>Its tag's color, as <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = "";

    /// <summary>Warn once a day when its estimated spend that day passes this many US dollars. Null: no warning.</summary>
    public decimal? DailySpendAlert { get; set; }

    /// <summary>Warn once a month when its estimated spend that month passes this many US dollars. Null: no warning.</summary>
    public decimal? MonthlySpendAlert { get; set; }

    /// <summary>The local day (<c>yyyy-MM-dd</c>) the daily warning last went out for, so a restart doesn't repeat it.</summary>
    public string? DailyAlertSentFor { get; set; }

    /// <summary>The local month (<c>yyyy-MM</c>) the monthly warning last went out for.</summary>
    public string? MonthlyAlertSentFor { get; set; }

    public bool IsApiKey => Kind == AccountKinds.ApiKey;
}

/// <summary>The kinds of <see cref="ClaudeAccount"/>, as they're saved.</summary>
public static class AccountKinds
{
    /// <summary>An Anthropic API key, which Claude Code asks Claudette for through its <c>apiKeyHelper</c>.</summary>
    public const string ApiKey = "apiKey";
}
