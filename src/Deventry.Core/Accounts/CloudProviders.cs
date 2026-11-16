using System.Text.RegularExpressions;

namespace Deventry.Core.Accounts;

/// <summary>A setting a cloud provider needs, such as its region: what Settings calls it, and the variable that carries it.</summary>
/// <param name="Example">What the box shows while it's empty.</param>
public sealed record CloudField(string Label, string Variable, bool Required, string Example);

/// <summary>
/// A cloud provider Claude Code can reach Claude through instead of Anthropic's API (DESIGN.md §4, "Accounts"): the
/// variable that turns it on, what it needs and how it signs in.
/// </summary>
public sealed record CloudProvider
{
    /// <summary>The account kind (<see cref="AccountKinds"/>), which is the <c>apiProvider</c> Claude Code reports for it.</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>The variable that turns it on, such as <c>CLAUDE_CODE_USE_BEDROCK</c>.</summary>
    public required string Switch { get; init; }

    /// <summary>How it can sign in (<see cref="CloudSignIns"/>), the first the one offered first.</summary>
    public required IReadOnlyList<string> SignIns { get; init; }

    /// <summary>The variable a <see cref="CloudSignIns.ApiKey"/> sign-in's key goes in; null without one.</summary>
    public string? KeyVariable { get; init; }

    /// <summary>What its key is called, such as "Bedrock API key".</summary>
    public string? KeyName { get; init; }

    /// <summary>Variables that would take over from a sign-in without a key, emptied for one.</summary>
    public IReadOnlyList<string> KeyVariables { get; init; } = [];

    public CloudField? Region { get; init; }

    public CloudField? Project { get; init; }

    public CloudField? Workspace { get; init; }

    public CloudField? Resource { get; init; }

    /// <summary>Claude Code's page about setting it up.</summary>
    public required string DocsUrl { get; init; }

    /// <summary>It signs in through AWS, so a profile and <c>awsAuthRefresh</c> apply.</summary>
    public bool IsAws => SignIns.Contains(CloudSignIns.Profile);

    /// <summary>It signs in through Google Cloud, so gcloud's sign-in, a key file and <c>gcpAuthRefresh</c> apply.</summary>
    public bool IsGoogle => SignIns.Contains(CloudSignIns.Gcloud);

    /// <summary>The fields it has, in the order Settings shows them.</summary>
    public IEnumerable<CloudField> Fields => new[] { Resource, Project, Workspace, Region }.OfType<CloudField>();
}

/// <summary>How a cloud provider account signs in, as the words <see cref="CloudAccountSettings.SignIn"/> saves.</summary>
public static class CloudSignIns
{
    /// <summary>
    /// An AWS profile from <c>~/.aws</c>, SSO's or an access key's that <c>aws configure</c> saved, or with no profile the
    /// default credential chain. Deventry keeps no AWS secret: Claude Code's <c>awsCredentialExport</c> takes only
    /// temporary credentials, with a session token, which an access key doesn't have.
    /// </summary>
    public const string Profile = "awsProfile";

    /// <summary>The provider's own API key, in the credential store, which goes in the environment of the tab's <c>claude</c>.</summary>
    public const string ApiKey = "apiKey";

    /// <summary>Google's Application Default Credentials, from <c>gcloud auth application-default login</c>.</summary>
    public const string Gcloud = "gcloud";

    /// <summary>A Google service account key file (<c>GOOGLE_APPLICATION_CREDENTIALS</c>).</summary>
    public const string KeyFile = "keyFile";

    /// <summary>Microsoft Entra ID through the Azure credential chain, such as <c>az login</c>.</summary>
    public const string EntraId = "entraId";

    /// <summary>Deventry keeps a secret for it in the credential store.</summary>
    public static bool KeepsSecret(string signIn) => signIn == ApiKey;

    /// <summary>Its secret goes in the environment of the tab's <c>claude</c>, where every command Claude runs can read it.</summary>
    public static bool PutsKeyInEnvironment(string signIn) => signIn == ApiKey;

    /// <summary>What Settings calls it for <paramref name="provider"/>.</summary>
    public static string Label(string signIn, CloudProvider provider) => signIn switch
    {
        Profile => "AWS profile or SSO",
        ApiKey => provider.KeyName ?? "API key",
        Gcloud => "gcloud sign-in",
        KeyFile => "Service account key file",
        EntraId => "Microsoft Entra ID (az login)",
        _ => signIn,
    };
}

/// <summary>The cloud providers accounts can use (DESIGN.md §4, "Accounts"), and checking what an account says about one.</summary>
public static partial class CloudProviders
{
    public static readonly CloudProvider Bedrock = new()
    {
        Kind = AccountKinds.Bedrock,
        Name = "Amazon Bedrock",
        Switch = "CLAUDE_CODE_USE_BEDROCK",
        SignIns = [CloudSignIns.Profile, CloudSignIns.ApiKey],
        KeyVariable = "AWS_BEARER_TOKEN_BEDROCK",
        KeyName = "Bedrock API key",
        KeyVariables = ["AWS_BEARER_TOKEN_BEDROCK"],
        Region = new CloudField("Region", "AWS_REGION", true, "us-east-1"),
        DocsUrl = "https://code.claude.com/docs/en/amazon-bedrock",
    };

    public static readonly CloudProvider AnthropicAws = new()
    {
        Kind = AccountKinds.AnthropicAws,
        Name = "Claude Platform on AWS",
        Switch = "CLAUDE_CODE_USE_ANTHROPIC_AWS",
        SignIns = [CloudSignIns.Profile, CloudSignIns.ApiKey],
        KeyVariable = "ANTHROPIC_AWS_API_KEY",
        KeyName = "Workspace API key",
        KeyVariables = ["ANTHROPIC_AWS_API_KEY"],
        Workspace = new CloudField("Workspace ID", "ANTHROPIC_AWS_WORKSPACE_ID", true, "wrkspc_01ABCDEFGHIJKLMN"),
        Region = new CloudField("Region", "AWS_REGION", true, "us-east-1"),
        DocsUrl = "https://code.claude.com/docs/en/claude-platform-on-aws",
    };

    public static readonly CloudProvider Vertex = new()
    {
        Kind = AccountKinds.Vertex,
        Name = "Google Cloud's Agent Platform",
        Switch = "CLAUDE_CODE_USE_VERTEX",
        SignIns = [CloudSignIns.Gcloud, CloudSignIns.KeyFile],
        Project = new CloudField("Project ID", "ANTHROPIC_VERTEX_PROJECT_ID", true, "my-project"),
        Region = new CloudField("Region", "CLOUD_ML_REGION", true, "global"),
        DocsUrl = "https://code.claude.com/docs/en/google-vertex-ai",
    };

    public static readonly CloudProvider AnthropicGoogleCloud = new()
    {
        Kind = AccountKinds.AnthropicGoogleCloud,
        Name = "Claude Models on Google Cloud",
        Switch = "CLAUDE_CODE_USE_ANTHROPIC_GOOGLE_CLOUD",
        SignIns = [CloudSignIns.Gcloud, CloudSignIns.KeyFile],
        Project = new CloudField("Project ID", "ANTHROPIC_GOOGLE_CLOUD_PROJECT", true, "my-project"),
        Workspace = new CloudField("Workspace ID", "ANTHROPIC_GOOGLE_CLOUD_WORKSPACE_ID", true, "wrkspc_01ABCDEFGHIJKLMN"),
        Region = new CloudField("Location", "ANTHROPIC_GOOGLE_CLOUD_LOCATION", false, "global"),
        DocsUrl = "https://code.claude.com/docs/en/claude-models-on-google-cloud-anthropic-managed",
    };

    public static readonly CloudProvider Foundry = new()
    {
        Kind = AccountKinds.Foundry,
        Name = "Microsoft Foundry",
        Switch = "CLAUDE_CODE_USE_FOUNDRY",
        SignIns = [CloudSignIns.EntraId, CloudSignIns.ApiKey],
        KeyVariable = "ANTHROPIC_FOUNDRY_API_KEY",
        KeyName = "Foundry API key",
        // A bearer token takes over from the key and from Entra ID alike.
        KeyVariables = ["ANTHROPIC_FOUNDRY_API_KEY", "ANTHROPIC_FOUNDRY_AUTH_TOKEN"],
        Resource = new CloudField("Resource name", "ANTHROPIC_FOUNDRY_RESOURCE", true, "my-resource"),
        DocsUrl = "https://code.claude.com/docs/en/microsoft-foundry",
    };

    /// <summary>Every provider, in the order Settings lists them.</summary>
    public static readonly IReadOnlyList<CloudProvider> All = [Bedrock, AnthropicAws, Vertex, AnthropicGoogleCloud, Foundry];

    /// <summary>
    /// The variables that turn a cloud provider on, Bedrock's Mantle endpoint's too: a tab under one of Deventry's
    /// accounts turns off every one but its own, wherever the user set them.
    /// </summary>
    public static readonly IReadOnlyList<string> Switches =
        [.. All.Select(p => p.Switch).Prepend("CLAUDE_CODE_USE_MANTLE").Order(StringComparer.Ordinal)];

    public static CloudProvider? Find(string? kind) => All.FirstOrDefault(p => p.Kind == kind);

    /// <summary>
    /// Whether Claude Code's <c>apiProvider</c> (<c>claude auth status</c>, a session's <c>initialize</c>) is the one an
    /// account expects: its provider's, or <c>firstParty</c> for an API key. Bedrock's Mantle endpoint counts as Bedrock.
    /// Unknown, an empty or missing value, counts as the expected one.
    /// </summary>
    public static bool IsExpected(ClaudeAccount account, string? apiProvider) =>
        apiProvider is not { Length: > 0 }
        || (account.Provider is { } provider
            ? apiProvider == provider.Kind || provider == Bedrock && apiProvider == "mantle"
            : apiProvider == "firstParty");

    /// <summary>What Claude Code's <c>apiProvider</c> is called, for saying a tab is on another than its account's.</summary>
    public static string NameOf(string apiProvider) => apiProvider switch
    {
        "firstParty" => "Anthropic's API",
        "mantle" => "Amazon Bedrock (Mantle)",
        "gateway" => "a Claude apps gateway",
        _ => Find(apiProvider)?.Name ?? apiProvider,
    };

    // ---- Checking what an account says -----------------------------------------------------------------------------

    /// <summary>
    /// What's wrong with <paramref name="cloud"/> for <paramref name="provider"/>, as Settings says it; null when it's
    /// fine. Doesn't check its key, which isn't in the settings.
    /// </summary>
    public static string? Problem(CloudProvider provider, CloudAccountSettings cloud)
    {
        if (!provider.SignIns.Contains(cloud.SignIn))
        {
            return $"Choose how {provider.Name} signs in.";
        }
        foreach (var field in provider.Fields)
        {
            var value = Value(field, provider, cloud);
            if (value is null)
            {
                if (field.Required)
                {
                    return $"Fill in the {Lower(field.Label)}.";
                }
                continue;
            }
            if (field == provider.Region && !IsRegion(value))
            {
                return $"The {Lower(field.Label)} is a name such as {field.Example}, in lower-case letters, digits and dashes.";
            }
            if (field == provider.Resource && !IsResourceName(value))
            {
                return $"The resource name is the name alone, such as {field.Example}: not a URL or a host name.";
            }
            if (value.Any(char.IsWhiteSpace))
            {
                return $"The {Lower(field.Label)} has a space in it.";
            }
        }
        if (cloud.SignIn == CloudSignIns.Profile && cloud.Profile is { Length: > 0 } profile && !IsProfileName(profile))
        {
            return "The AWS profile's name can only have letters, digits, dots, dashes and underscores here.";
        }
        if (cloud.SignIn == CloudSignIns.KeyFile && cloud.KeyFile is not { Length: > 0 })
        {
            return "Choose the service account key file.";
        }
        if (new[] { cloud.FableModel, cloud.OpusModel, cloud.SonnetModel, cloud.HaikuModel }.Any(m => m is not null && m.Any(char.IsWhiteSpace)))
        {
            return "A model has a space in it.";
        }
        return null;
    }

    /// <summary>A label within a sentence: "Region" as "region", "Project ID" as "project ID".</summary>
    private static string Lower(string label) => label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];

    /// <summary>The value <paramref name="cloud"/> has for <paramref name="field"/>, trimmed; null when it's empty.</summary>
    public static string? Value(CloudField field, CloudProvider provider, CloudAccountSettings cloud) =>
        (field == provider.Region ? cloud.Region
            : field == provider.Project ? cloud.Project
            : field == provider.Workspace ? cloud.Workspace
            : field == provider.Resource ? cloud.Resource
            : null)?.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>A region or location as Claude Code takes one: <c>us-east-1</c>, <c>global</c>, <c>eu</c>. It treats one with a dot, slash or space as unset.</summary>
    public static bool IsRegion(string value) => RegionPattern().IsMatch(value);

    /// <summary>A Foundry resource's name alone: Claude Code refuses a URL or a host name.</summary>
    public static bool IsResourceName(string value) => ResourcePattern().IsMatch(value);

    /// <summary>A profile name the sign-in command can carry as it is, through any shell.</summary>
    public static bool IsProfileName(string value) => ProfilePattern().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$")]
    private static partial Regex RegionPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,63}$")]
    private static partial Regex ResourcePattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ProfilePattern();
}
