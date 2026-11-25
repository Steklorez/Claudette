namespace Deventry.Core.Accounts;

/// <summary>
/// An account a tab can run Claude Code under besides the Default one, Claude Code's own sign-in (DESIGN.md §4,
/// "Accounts"): an Anthropic API key, a cloud provider, or a model on the user's own Ollama or <c>llama-server</c>
/// (DESIGN.md §18, "Ollama and llama.cpp accounts"). Kept per machine in the app state; its key, if it has one, is only in
/// the OS credential store.
/// </summary>
public sealed class ClaudeAccount
{
    /// <summary>Letters and digits only, so it can go in the key helper's command (<see cref="ApiKeyHelper.Command"/>).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>
    /// How the account signs in, as a word (<see cref="AccountKinds"/>): a kind a later Deventry adds reads as one this
    /// one doesn't know, rather than failing the whole file.
    /// </summary>
    public string Kind { get; set; } = AccountKinds.ApiKey;

    /// <summary>A cloud provider account's details; null for an API key.</summary>
    public CloudAccountSettings? Cloud { get; set; }

    /// <summary>An Ollama or llama.cpp account's server and model; null for any other kind.</summary>
    public LocalServerSettings? Local { get; set; }

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

    /// <summary>The cloud provider Claude Code reaches Claude through for it; null for an API key, or a kind this Deventry doesn't know.</summary>
    public CloudProvider? Provider => Cloud is null ? null : CloudProviders.Find(Kind);

    public bool IsCloudProvider => Provider is not null;

    /// <summary>The server its model runs on, for an Ollama or llama.cpp account; null for any other kind.</summary>
    public LocalServerKind? Server => Local is null ? null : LocalServers.Find(Kind);

    /// <summary>Its model runs on the user's own Ollama or <c>llama-server</c>, which charges nothing.</summary>
    public bool IsLocal => Server is not null;

    /// <summary>A kind this Deventry knows, with what it needs: tabs can run under it.</summary>
    public bool IsUsable => IsApiKey || IsCloudProvider || IsLocal;

    /// <summary>
    /// It has a secret in the credential store: an API key's key, a cloud provider's, or an Ollama or llama.cpp server's
    /// (<see cref="AccountSecrets"/>).
    /// </summary>
    public bool HasSecret => IsApiKey || Cloud is { } cloud && CloudSignIns.KeepsSecret(cloud.SignIn) || Local is { HasKey: true };

    /// <summary>What it is, as the info card and Settings say it: "API key", "Amazon Bedrock", "Ollama".</summary>
    public string KindName => Provider?.Name ?? Server?.Name ?? "API key";
}

/// <summary>
/// The kinds of <see cref="ClaudeAccount"/>, as they're saved. A cloud provider's word is the <c>apiProvider</c>
/// Claude Code reports for it.
/// </summary>
public static class AccountKinds
{
    /// <summary>An Anthropic API key, which Claude Code asks Deventry for through its <c>apiKeyHelper</c>.</summary>
    public const string ApiKey = "apiKey";

    /// <summary>Amazon Bedrock (<c>CLAUDE_CODE_USE_BEDROCK</c>).</summary>
    public const string Bedrock = "bedrock";

    /// <summary>Claude Platform on AWS (<c>CLAUDE_CODE_USE_ANTHROPIC_AWS</c>).</summary>
    public const string AnthropicAws = "anthropicAws";

    /// <summary>Google Cloud's Agent Platform, formerly Vertex AI (<c>CLAUDE_CODE_USE_VERTEX</c>).</summary>
    public const string Vertex = "vertex";

    /// <summary>Claude Models on Google Cloud, Anthropic managed (<c>CLAUDE_CODE_USE_ANTHROPIC_GOOGLE_CLOUD</c>).</summary>
    public const string AnthropicGoogleCloud = "anthropicGoogleCloud";

    /// <summary>Microsoft Foundry (<c>CLAUDE_CODE_USE_FOUNDRY</c>).</summary>
    public const string Foundry = "foundry";

    /// <summary>A model on the user's own Ollama (DESIGN.md §18, "Ollama and llama.cpp accounts").</summary>
    public const string Ollama = "ollama";

    /// <summary>A model on the user's own <c>llama-server</c>, llama.cpp's.</summary>
    public const string LlamaCpp = "llamaCpp";
}

/// <summary>
/// What a cloud provider account says about reaching it (DESIGN.md §4, "Accounts"). Nothing here is secret: a key is in
/// the OS credential store. Which fields apply depends on the provider (<see cref="CloudProvider"/>).
/// </summary>
public sealed class CloudAccountSettings
{
    /// <summary>How it signs in, as a word (<see cref="CloudSignIns"/>).</summary>
    public string SignIn { get; set; } = "";

    /// <summary>The AWS region, the Agent Platform's region, or the Anthropic-managed Google Cloud location.</summary>
    public string? Region { get; set; }

    /// <summary>The Google Cloud project's id.</summary>
    public string? Project { get; set; }

    /// <summary>The workspace id (<c>wrkspc_…</c>) of Claude Platform on AWS or Anthropic-managed Google Cloud.</summary>
    public string? Workspace { get; set; }

    /// <summary>The Microsoft Foundry resource's name.</summary>
    public string? Resource { get; set; }

    /// <summary>The AWS profile; null for the default credential chain, as the environment has it.</summary>
    public string? Profile { get; set; }

    /// <summary>The Google service account key file, for <see cref="CloudSignIns.KeyFile"/>.</summary>
    public string? KeyFile { get; set; }

    /// <summary>Let Claude Code run the provider's sign-in command when the credentials expire (<c>awsAuthRefresh</c>, <c>gcpAuthRefresh</c>).</summary>
    public bool RefreshSignIn { get; set; }

    /// <summary>The model the <c>fable</c> alias uses; null for Claude Code's default on the provider.</summary>
    public string? FableModel { get; set; }

    public string? OpusModel { get; set; }

    public string? SonnetModel { get; set; }

    public string? HaikuModel { get; set; }
}
