using System.Text.Json.Nodes;
using Deventry.Core.Claude;

namespace Deventry.Core.Accounts;

/// <summary>
/// What a tab's <c>claude</c> starts with for one of Deventry's accounts (DESIGN.md §4, "Accounts"): the
/// <c>--settings</c> value, and changes to its environment.
/// <para>
/// The settings' <c>env</c> says which provider the tab uses and how it's set up, over Claude Code's settings files and
/// the environment, which the Default account's configuration may be in: an empty value turns a variable off. An API
/// key reaches Claude Code through Deventry's key helper (<see cref="ApiKeyHelper"/>). A provider's own key can only
/// reach Claude Code in its environment, so only that goes there, and never in the settings, which are on the command
/// line.
/// </para>
/// </summary>
/// <param name="Settings">The <c>--settings</c> value: no secrets.</param>
/// <param name="Environment">Variables to set, or with a null value to remove, in the tab's <c>claude</c>'s environment.</param>
/// <param name="KeyVariable">The variable the account's key is in, in <paramref name="Environment"/>; null when it has none there.</param>
public sealed record AccountLaunch(string Settings, IReadOnlyDictionary<string, string?> Environment, string? KeyVariable = null)
{
    /// <summary>
    /// Anthropic credentials Claude Code takes over an <c>apiKeyHelper</c>, so one in the user's environment or settings
    /// would quietly bill another key: every account's tabs go without them.
    /// </summary>
    public static readonly IReadOnlyList<string> AnthropicCredentials = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    /// <summary>AWS credentials in the environment, which the credential chain takes over a profile.</summary>
    public static readonly IReadOnlyList<string> AwsEnvironmentCredentials = ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN"];

    /// <summary>The variables that pin each model alias on a cloud provider, by what <see cref="CloudAccountSettings"/> calls them.</summary>
    public static readonly IReadOnlyList<string> ModelPins =
        ["ANTHROPIC_DEFAULT_FABLE_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL"];

    /// <summary>The sign-in command for an AWS profile, which Claude Code runs as <c>awsAuthRefresh</c>.</summary>
    public static string AwsSignInCommand(string? profile) => profile is { Length: > 0 } ? $"aws sso login --profile {profile}" : "aws sso login";

    /// <summary>The sign-in command for Google's Application Default Credentials, which Claude Code runs as <c>gcpAuthRefresh</c>.</summary>
    public const string GcloudSignInCommand = "gcloud auth application-default login";

    /// <summary>What a tab on <paramref name="account"/> starts with.</summary>
    /// <param name="executable">Deventry's own executable, which the key helper runs.</param>
    /// <param name="key">
    /// The account's key from the credential store, for a cloud account whose key goes in the environment
    /// (<see cref="CloudSignIns.PutsKeyInEnvironment"/>); otherwise unused.
    /// </param>
    /// <exception cref="ArgumentException">Deventry's path or the account's id can't go in the helper's command.</exception>
    /// <exception cref="InvalidOperationException">The account is of a kind this Deventry can't use, or its key is missing.</exception>
    public static AccountLaunch For(ClaudeAccount account, string executable, string? key = null)
    {
        var env = new JsonObject();
        var settings = new JsonObject();
        var environment = new Dictionary<string, string?>();
        void Off(string variable)
        {
            env[variable] = "";
            environment[variable] = null;
        }
        void OffAll(IEnumerable<string> variables)
        {
            foreach (var variable in variables)
            {
                Off(variable);
            }
        }
        void Set(string variable, string? value)
        {
            if (value?.Trim() is { Length: > 0 } trimmed)
            {
                env[variable] = trimmed;
            }
            else
            {
                Off(variable);
            }
        }

        if (account.IsApiKey)
        {
            settings["apiKeyHelper"] = ApiKeyHelper.Command(executable, account.Id);
            OffAll(CloudProviders.Switches.Concat(AnthropicCredentials));
            settings["env"] = env;
            return new AccountLaunch(settings.ToJsonString(), environment);
        }
        if (account is not { Provider: { } provider, Cloud: { } cloud })
        {
            throw new InvalidOperationException($"{account.Name} is a kind of account this Deventry doesn't know.");
        }

        OffAll(CloudProviders.Switches.Where(s => s != provider.Switch).Concat(AnthropicCredentials));
        env[provider.Switch] = "1";
        foreach (var field in provider.Fields)
        {
            Set(field.Variable, CloudProviders.Value(field, provider, cloud));
        }
        // The account says its models: another provider's pins in the user's settings would name models this one hasn't.
        Set(ModelPins[0], cloud.FableModel);
        Set(ModelPins[1], cloud.OpusModel);
        Set(ModelPins[2], cloud.SonnetModel);
        Set(ModelPins[3], cloud.HaikuModel);

        string? keyVariable = null;
        switch (cloud.SignIn)
        {
            case CloudSignIns.Profile:
                OffAll(provider.KeyVariables);
                if (cloud.Profile?.Trim() is { Length: > 0 } profile)
                {
                    env["AWS_PROFILE"] = profile;
                    OffAll(AwsEnvironmentCredentials);
                }
                if (cloud.RefreshSignIn)
                {
                    settings["awsAuthRefresh"] = AwsSignInCommand(cloud.Profile?.Trim());
                }
                break;
            case CloudSignIns.ApiKey when provider.KeyVariable is { } variable:
                if (key?.Trim() is not { Length: > 0 } trimmedKey)
                {
                    throw new InvalidOperationException($"Deventry has no {provider.KeyName} for {account.Name}.");
                }
                OffAll(provider.KeyVariables.Where(v => v != variable));
                environment[variable] = trimmedKey;
                keyVariable = variable;
                break;
            case CloudSignIns.Gcloud:
                if (cloud.RefreshSignIn)
                {
                    settings["gcpAuthRefresh"] = GcloudSignInCommand;
                }
                break;
            case CloudSignIns.KeyFile:
                env["GOOGLE_APPLICATION_CREDENTIALS"] = cloud.KeyFile?.Trim() ?? "";
                break;
            case CloudSignIns.EntraId:
                OffAll(provider.KeyVariables);
                break;
            default:
                throw new InvalidOperationException($"{account.Name} signs in to {provider.Name} in a way this Deventry doesn't know.");
        }
        settings["env"] = env;
        return new AccountLaunch(settings.ToJsonString(), environment, keyVariable);
    }

    /// <summary>
    /// The first of Claude Code's settings files whose <c>env</c> sets <paramref name="variable"/>, which then takes over
    /// from the value in the environment a tab's <c>claude</c> starts with; null when none does.
    /// </summary>
    /// <param name="files">The settings files, as <see cref="StartingPermissionMode.SettingsFiles"/> lists them.</param>
    public static string? SettingsFileSetting(IEnumerable<ClaudeSettingsFile> files, string variable) =>
        files.FirstOrDefault(f => ClaudeSettingsFiles.Read(f.Path)?["env"] is JsonObject env
            && env[variable] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)?.Path;
}
