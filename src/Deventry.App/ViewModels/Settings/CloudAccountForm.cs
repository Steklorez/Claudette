using Deventry.Core.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A way to sign in to a cloud provider, as Settings → Claude Code → Accounts offers it.</summary>
public sealed record SignInChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// What a cloud provider account says, in Settings → Claude Code → Accounts (DESIGN.md §4, "Accounts"): how it signs in, the
/// provider's own settings, the models it pins, and, while adding one, its key. The same form changes an account's
/// settings, without its way of signing in, which a key may go with.
/// </summary>
public sealed partial class CloudAccountForm : ObservableObject
{
    private readonly Func<string, Task<string?>> _pickFile;
    private readonly Func<string, Task> _openUrl;

    /// <param name="from">An account's settings, to change them; null for a new account.</param>
    /// <param name="credentialStore">What the credential store is called, for saying where a key is kept.</param>
    public CloudAccountForm(CloudProvider provider, string credentialStore, Func<string, Task<string?>> pickFile, Func<string, Task> openUrl, CloudAccountSettings? from = null)
    {
        Provider = provider;
        CredentialStore = credentialStore;
        _pickFile = pickFile;
        _openUrl = openUrl;
        IsNew = from is null;
        SignIns = [.. provider.SignIns.Select(s => new SignInChoice(s, CloudSignIns.Label(s, provider)))];
        SignIn = SignIns.FirstOrDefault(s => s.Value == from?.SignIn) ?? SignIns[0];
        Region = from?.Region ?? "";
        Project = from?.Project ?? "";
        Workspace = from?.Workspace ?? "";
        Resource = from?.Resource ?? "";
        Profile = from?.Profile ?? "";
        KeyFile = from?.KeyFile ?? "";
        // On by default for the sign-ins that expire: SSO and gcloud's.
        RefreshSignIn = from?.RefreshSignIn ?? true;
        FableModel = from?.FableModel ?? "";
        OpusModel = from?.OpusModel ?? "";
        SonnetModel = from?.SonnetModel ?? "";
        HaikuModel = from?.HaikuModel ?? "";
    }

    public CloudProvider Provider { get; }

    public string CredentialStore { get; }

    /// <summary>A new account's form, which asks for its key; an account's own can't change how it signs in.</summary>
    public bool IsNew { get; }

    // ---- Signing in ---------------------------------------------------------------------------------------------------

    public IReadOnlyList<SignInChoice> SignIns { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfile), nameof(IsKeyFile), nameof(AsksApiKey), nameof(ExposesKey),
        nameof(KeepsSecret), nameof(CanRefreshSignIn), nameof(RefreshText), nameof(SignInNote), nameof(ExposureWarning))]
    public partial SignInChoice SignIn { get; set; }

    public bool CanChooseSignIn => IsNew && SignIns.Count > 1;

    /// <summary>What the account's way of signing in is, for an account's own form, where it can't change.</summary>
    public string SignInText => SignIn.Label;

    public bool IsProfile => SignIn.Value == CloudSignIns.Profile;

    public bool IsKeyFile => SignIn.Value == CloudSignIns.KeyFile;

    /// <summary>The way to sign in has a secret Deventry keeps in the credential store.</summary>
    public bool KeepsSecret => CloudSignIns.KeepsSecret(SignIn.Value);

    public bool AsksApiKey => IsNew && SignIn.Value == CloudSignIns.ApiKey;

    /// <summary>The key goes in the environment of the account's tabs' <c>claude</c>, where the commands Claude runs can read it.</summary>
    public bool ExposesKey => CloudSignIns.PutsKeyInEnvironment(SignIn.Value);

    public string KeyName => Provider.KeyName ?? "API key";

    /// <summary>
    /// What putting the key in the environment exposes it to, said before the key is added (DESIGN.md §4, "Accounts"):
    /// the provider reads it from nowhere else.
    /// </summary>
    public string ExposureWarning =>
        $"Claude Code reads a {KeyName} only from its environment, so Deventry puts this key in the environment of the claude that each of this account's tabs runs. " +
        "Every command Claude runs there inherits it: its Bash commands and scripts, hooks, MCP servers, and anything they start. Any of them can read the key, and print, log or send it on, " +
        "and a prompt that tells Claude to read it gets it. " +
        $"Deventry keeps it in {CredentialStore} otherwise, never in its own files, a command line or a log. " +
        $"Where you can, sign in another way instead: {Alternatives}.";

    /// <summary>The ways to sign in that keep the key from the commands Claude runs.</summary>
    private string Alternatives => string.Join(", or ", SignIns.Where(s => !CloudSignIns.PutsKeyInEnvironment(s.Value)).Select(s => s.Label));

    /// <summary>The user read <see cref="ExposureWarning"/> and adds the key anyway.</summary>
    [ObservableProperty]
    public partial bool AcceptsExposure { get; set; }

    /// <summary>What the chosen way to sign in needs before a tab starts, or how the key is kept.</summary>
    public string? SignInNote => SignIn.Value switch
    {
        CloudSignIns.Profile => "A profile from ~/.aws, such as one for IAM Identity Center (SSO). For an access key, save it to a profile with \"aws configure --profile <name>\" and name that profile here. Leave it empty for the default credential chain, as a terminal has it.",
        CloudSignIns.Gcloud => $"Application Default Credentials: sign in once with \"{AccountLaunch.GcloudSignInCommand}\".",
        CloudSignIns.KeyFile => "A service account's JSON key file. Claude Code reads it where it is; it's the file's to keep safe.",
        CloudSignIns.EntraId => "Claude Code uses the Azure credential chain: sign in with \"az login\" before a tab starts.",
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshText))]
    public partial string Profile { get; set; }

    [ObservableProperty]
    public partial string KeyFile { get; set; }

    /// <summary>Claude Code runs the provider's sign-in command when the credentials expire.</summary>
    [ObservableProperty]
    public partial bool RefreshSignIn { get; set; }

    public bool CanRefreshSignIn => SignIn.Value is CloudSignIns.Profile or CloudSignIns.Gcloud;

    public string RefreshText =>
        $"Sign in again when the credentials expire ({(IsProfile ? AccountLaunch.AwsSignInCommand(Profile.Trim()) : AccountLaunch.GcloudSignInCommand)}, which opens the browser)";

    [RelayCommand]
    private async Task BrowseKeyFileAsync()
    {
        if (await _pickFile("Choose the service account key file") is { } path)
        {
            KeyFile = path;
        }
    }

    // ---- The provider's settings ---------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string Region { get; set; }

    [ObservableProperty]
    public partial string Project { get; set; }

    [ObservableProperty]
    public partial string Workspace { get; set; }

    [ObservableProperty]
    public partial string Resource { get; set; }

    public bool HasRegion => Provider.Region is not null;

    public bool HasProject => Provider.Project is not null;

    public bool HasWorkspace => Provider.Workspace is not null;

    public bool HasResource => Provider.Resource is not null;

    public string RegionLabel => Label(Provider.Region);

    public string ProjectLabel => Label(Provider.Project);

    public string WorkspaceLabel => Label(Provider.Workspace);

    public string ResourceLabel => Label(Provider.Resource);

    public string RegionExample => Provider.Region?.Example ?? "";

    public string ProjectExample => Provider.Project?.Example ?? "";

    public string WorkspaceExample => Provider.Workspace?.Example ?? "";

    public string ResourceExample => Provider.Resource?.Example ?? "";

    private static string Label(CloudField? field) => field is null ? "" : field.Required ? field.Label : $"{field.Label} (optional)";

    // ---- Models --------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string FableModel { get; set; }

    [ObservableProperty]
    public partial string OpusModel { get; set; }

    [ObservableProperty]
    public partial string SonnetModel { get; set; }

    [ObservableProperty]
    public partial string HaikuModel { get; set; }

    public string ModelPlaceholder => $"Claude Code's default on {Provider.Name}";

    public string ModelsNote => Provider == CloudProviders.Foundry
        ? "Your deployments' names, which Claude Code calls for each model. Without them it tries its own defaults, which your resource may not have."
        : $"The model ids each of Claude Code's model choices uses on {Provider.Name}. Leave them empty for Claude Code's defaults, which may not be enabled for you yet.";

    [RelayCommand]
    private Task OpenDocsAsync() => _openUrl(Provider.DocsUrl);

    public string DocsText => $"How to set up {Provider.Name}";

    // ---- The key, while adding -------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string ApiKey { get; set; } = "";

    /// <summary>The key to keep for the account; null when it has none, or it isn't filled in.</summary>
    public string? Secret => KeepsSecret && ApiKey.Trim() is { Length: > 0 } key ? key : null;

    /// <summary>What's missing from a new account's key; null when it's there or none is needed.</summary>
    public string? SecretProblem => !KeepsSecret ? null
        : Secret is null ? $"Paste the {KeyName}."
        : ExposesKey && !AcceptsExposure ? $"Read what putting the {KeyName} in the environment exposes it to, and tick the box under it to add it anyway."
        : null;

    /// <summary>Forgets the key typed in, once it's saved or the form is left.</summary>
    public void ClearSecrets()
    {
        ApiKey = "";
        AcceptsExposure = false;
    }

    // ---- What it says ----------------------------------------------------------------------------------------------------

    /// <summary>The account's settings as the form says them.</summary>
    public CloudAccountSettings ToSettings() => new()
    {
        SignIn = SignIn.Value,
        Region = Trimmed(Region),
        Project = Trimmed(Project),
        Workspace = Trimmed(Workspace),
        Resource = Trimmed(Resource),
        Profile = IsProfile ? Trimmed(Profile) : null,
        KeyFile = IsKeyFile ? Trimmed(KeyFile) : null,
        RefreshSignIn = CanRefreshSignIn && RefreshSignIn,
        FableModel = Trimmed(FableModel),
        OpusModel = Trimmed(OpusModel),
        SonnetModel = Trimmed(SonnetModel),
        HaikuModel = Trimmed(HaikuModel),
    };

    /// <summary>What's wrong with the settings, as Settings says it; null when they're fine.</summary>
    public string? Problem => CloudProviders.Problem(Provider, ToSettings());

    private static string? Trimmed(string? value) => value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
