using System.Text.Json.Nodes;

namespace Claudette.Core.Accounts;

/// <summary>
/// How an API-key account's key reaches Claude Code (DESIGN.md §4, "Accounts"). The key stays in the OS credential
/// store. Claude Code runs the <c>apiKeyHelper</c> command Claudette passes it with <c>--settings</c>, which is Claudette
/// itself started with <see cref="Option"/>: it reads the key from the store and prints it, so the key is never in an
/// argument, a variable, a settings file or a log.
/// </summary>
public static class ApiKeyHelper
{
    /// <summary>The option Claudette is started with to print an account's key: <c>--api-key-helper &lt;account id&gt;</c>.</summary>
    public const string Option = "--api-key-helper";

    /// <summary>
    /// Variables an API-key account's tabs go without: Claude Code takes them over its <c>apiKeyHelper</c>, so one in the
    /// user's environment would quietly bill another key.
    /// </summary>
    public static readonly IReadOnlyList<string> OutrankingVariables = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    /// <summary>Where the account's key is in the OS credential store.</summary>
    public static string CredentialKey(string accountId) => $"claude-api-key/{accountId}";

    /// <summary>What the OS shows for the key.</summary>
    public static string CredentialLabel(string accountName) => $"Claudette: Claude API key ({accountName})";

    /// <summary>An id the helper's command can carry as it is: letters, digits and dashes.</summary>
    public static bool IsSafeId(string? id) => id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>
    /// A path a shell can run inside double quotes as it is: nothing <c>sh</c> or <c>cmd</c> would read as a quote, a
    /// variable or a command.
    /// </summary>
    public static bool IsSafePath(string? path) => path is { Length: > 0 } && path.IndexOfAny(['"', '$', '`', '%', '!', '\n', '\r']) < 0;

    /// <summary>The command Claude Code runs to get the key: Claudette at <paramref name="executable"/>, in helper mode.</summary>
    /// <exception cref="ArgumentException">The path or the id can't go in a command as they are.</exception>
    public static string Command(string executable, string accountId)
    {
        if (!IsSafePath(executable))
        {
            throw new ArgumentException($"Claudette's path can't go in a command as it is: {executable}", nameof(executable));
        }
        if (!IsSafeId(accountId))
        {
            throw new ArgumentException("The account's id can't go in a command as it is.", nameof(accountId));
        }
        return $"\"{executable}\" {Option} {accountId}";
    }

    /// <summary>The <c>--settings</c> value for an API-key account's tab: just its <c>apiKeyHelper</c>.</summary>
    public static string Settings(string executable, string accountId) =>
        new JsonObject { ["apiKeyHelper"] = Command(executable, accountId) }.ToJsonString();

    /// <summary>The account Claudette was started to print the key of, when its arguments are <see cref="Option"/> and an id.</summary>
    public static bool TryGetAccountId(IReadOnlyList<string> args, out string accountId)
    {
        accountId = "";
        if (args.Count == 2 && args[0] == Option && IsSafeId(args[1]))
        {
            accountId = args[1];
            return true;
        }
        return false;
    }
}
