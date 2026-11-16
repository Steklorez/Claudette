using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.Claude;
using Deventry.Core.Settings;

namespace Deventry.Core.Tests.Accounts;

/// <summary>Cloud provider accounts, and how a tab under one reaches its provider (DESIGN.md §4, "Accounts").</summary>
public class CloudAccountsTests
{
    private const string Id = "0a1b2c3d4e5f";
    private const string Deventry = "/opt/Deventry/Deventry";

    private static ClaudeAccount Account(CloudProvider provider, CloudAccountSettings cloud) =>
        new() { Id = Id, Name = "Studio", Kind = provider.Kind, Cloud = cloud };

    private static JsonObject Settings(AccountLaunch launch) => JsonNode.Parse(launch.Settings)!.AsObject();

    private static JsonObject Env(AccountLaunch launch) => Settings(launch)["env"]!.AsObject();

    private static string? Value(JsonObject env, string variable) => env[variable]?.GetValue<string>();

    // ---- The provider ------------------------------------------------------------------------------------------------

    public static TheoryData<string> Kinds => [.. CloudProviders.All.Select(p => p.Kind)];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_tab_turns_its_provider_on_and_every_other_one_and_Anthropics_credentials_off(string kind)
    {
        var provider = CloudProviders.Find(kind)!;
        var launch = AccountLaunch.For(Account(provider, Filled(provider)), Deventry);
        var env = Env(launch);

        Assert.Equal("1", Value(env, provider.Switch));
        foreach (var other in CloudProviders.Switches.Where(s => s != provider.Switch).Concat(AccountLaunch.AnthropicCredentials))
        {
            // Empty over Claude Code's settings files, which Default's provider may be in, and gone from the environment.
            Assert.Equal("", Value(env, other));
            Assert.True(launch.Environment.ContainsKey(other) && launch.Environment[other] is null, other);
        }
        foreach (var field in provider.Fields)
        {
            Assert.Equal(CloudProviders.Value(field, provider, Filled(provider)), Value(env, field.Variable));
        }
    }

    /// <summary>What <paramref name="provider"/> needs, filled in, with its first way to sign in that keeps no secret.</summary>
    private static CloudAccountSettings Filled(CloudProvider provider) => new()
    {
        SignIn = provider.SignIns.First(s => !CloudSignIns.KeepsSecret(s)),
        Region = provider.Region is null ? null : "us-east-1",
        Project = provider.Project is null ? null : "my-project",
        Workspace = provider.Workspace is null ? null : "wrkspc_01ABC",
        Resource = provider.Resource is null ? null : "my-resource",
    };

    [Fact]
    public void Each_providers_variables_are_the_ones_Claude_Code_documents()
    {
        Assert.Equal(
            ["CLAUDE_CODE_USE_ANTHROPIC_AWS", "CLAUDE_CODE_USE_ANTHROPIC_GOOGLE_CLOUD", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_FOUNDRY", "CLAUDE_CODE_USE_MANTLE", "CLAUDE_CODE_USE_VERTEX"],
            CloudProviders.Switches);
        Assert.Equal(["AWS_REGION"], CloudProviders.Bedrock.Fields.Select(f => f.Variable));
        Assert.Equal(["ANTHROPIC_AWS_WORKSPACE_ID", "AWS_REGION"], CloudProviders.AnthropicAws.Fields.Select(f => f.Variable));
        Assert.Equal(["ANTHROPIC_VERTEX_PROJECT_ID", "CLOUD_ML_REGION"], CloudProviders.Vertex.Fields.Select(f => f.Variable));
        Assert.Equal(["ANTHROPIC_GOOGLE_CLOUD_PROJECT", "ANTHROPIC_GOOGLE_CLOUD_WORKSPACE_ID", "ANTHROPIC_GOOGLE_CLOUD_LOCATION"], CloudProviders.AnthropicGoogleCloud.Fields.Select(f => f.Variable));
        Assert.Equal(["ANTHROPIC_FOUNDRY_RESOURCE"], CloudProviders.Foundry.Fields.Select(f => f.Variable));
    }

    [Fact]
    public void An_optional_field_left_empty_is_emptied_so_the_users_own_doesnt_apply()
    {
        var cloud = Filled(CloudProviders.AnthropicGoogleCloud);
        cloud.Region = " ";

        var launch = AccountLaunch.For(Account(CloudProviders.AnthropicGoogleCloud, cloud), Deventry);

        Assert.Equal("", Value(Env(launch), "ANTHROPIC_GOOGLE_CLOUD_LOCATION"));
        Assert.Null(CloudProviders.Problem(CloudProviders.AnthropicGoogleCloud, cloud));
    }

    [Fact]
    public void The_accounts_model_pins_are_set_and_the_ones_it_leaves_empty_are_emptied()
    {
        var cloud = Filled(CloudProviders.Bedrock);
        cloud.OpusModel = "us.anthropic.claude-opus-4-8";
        cloud.HaikuModel = " us.anthropic.claude-haiku-4-5-20251001-v1:0 ";

        var env = Env(AccountLaunch.For(Account(CloudProviders.Bedrock, cloud), Deventry));

        Assert.Equal("us.anthropic.claude-opus-4-8", Value(env, "ANTHROPIC_DEFAULT_OPUS_MODEL"));
        Assert.Equal("us.anthropic.claude-haiku-4-5-20251001-v1:0", Value(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL"));
        Assert.Equal("", Value(env, "ANTHROPIC_DEFAULT_SONNET_MODEL"));
        Assert.Equal("", Value(env, "ANTHROPIC_DEFAULT_FABLE_MODEL"));
    }

    // ---- Signing in ----------------------------------------------------------------------------------------------------

    [Fact]
    public void An_AWS_profile_is_used_over_credentials_in_the_environment_and_can_sign_in_again()
    {
        var cloud = Filled(CloudProviders.Bedrock);
        cloud.Profile = "work-sso";
        cloud.RefreshSignIn = true;

        var launch = AccountLaunch.For(Account(CloudProviders.Bedrock, cloud), Deventry);
        var env = Env(launch);

        Assert.Equal("work-sso", Value(env, "AWS_PROFILE"));
        Assert.All(AccountLaunch.AwsEnvironmentCredentials.Append("AWS_BEARER_TOKEN_BEDROCK"), v => Assert.Equal("", Value(env, v)));
        Assert.Equal("aws sso login --profile work-sso", Settings(launch)["awsAuthRefresh"]!.GetValue<string>());
        Assert.Null(launch.KeyVariable);
    }

    [Fact]
    public void Without_a_profile_the_default_chain_is_left_as_the_environment_has_it()
    {
        var cloud = Filled(CloudProviders.AnthropicAws);

        var launch = AccountLaunch.For(Account(CloudProviders.AnthropicAws, cloud), Deventry);
        var env = Env(launch);

        Assert.Null(Value(env, "AWS_PROFILE"));
        Assert.Null(Value(env, "AWS_ACCESS_KEY_ID"));
        // A workspace key takes over from AWS credentials.
        Assert.Equal("", Value(env, "ANTHROPIC_AWS_API_KEY"));
        Assert.Null(Settings(launch)["awsAuthRefresh"]);
    }

    [Theory]
    [InlineData(AccountKinds.Bedrock, "AWS_BEARER_TOKEN_BEDROCK")]
    [InlineData(AccountKinds.AnthropicAws, "ANTHROPIC_AWS_API_KEY")]
    [InlineData(AccountKinds.Foundry, "ANTHROPIC_FOUNDRY_API_KEY")]
    public void A_providers_API_key_goes_only_in_the_environment(string kind, string variable)
    {
        var provider = CloudProviders.Find(kind)!;
        var cloud = Filled(provider);
        cloud.SignIn = CloudSignIns.ApiKey;

        var launch = AccountLaunch.For(Account(provider, cloud), Deventry, key: " the-key ");

        Assert.Equal("the-key", launch.Environment[variable]);
        Assert.Equal(variable, launch.KeyVariable);
        Assert.DoesNotContain("the-key", launch.Settings, StringComparison.Ordinal);
        // Not even emptied in the settings, which would take over from the environment.
        Assert.False(Env(launch).ContainsKey(variable));
    }

    [Fact]
    public void A_Foundry_key_goes_without_the_bearer_token_that_would_take_over_from_it()
    {
        var cloud = Filled(CloudProviders.Foundry);
        cloud.SignIn = CloudSignIns.ApiKey;

        var launch = AccountLaunch.For(Account(CloudProviders.Foundry, cloud), Deventry, key: "k");

        Assert.Equal("", Value(Env(launch), "ANTHROPIC_FOUNDRY_AUTH_TOKEN"));
        Assert.Null(launch.Environment["ANTHROPIC_FOUNDRY_AUTH_TOKEN"]);
    }

    [Fact]
    public void Entra_ID_goes_without_any_Foundry_key_or_token()
    {
        var launch = AccountLaunch.For(Account(CloudProviders.Foundry, Filled(CloudProviders.Foundry)), Deventry);

        Assert.Equal("", Value(Env(launch), "ANTHROPIC_FOUNDRY_API_KEY"));
        Assert.Equal("", Value(Env(launch), "ANTHROPIC_FOUNDRY_AUTH_TOKEN"));
    }

    [Fact]
    public void A_key_that_isnt_there_stops_the_tab_rather_than_starting_it_without()
    {
        var cloud = Filled(CloudProviders.Bedrock);
        cloud.SignIn = CloudSignIns.ApiKey;

        var error = Assert.Throws<InvalidOperationException>(() => AccountLaunch.For(Account(CloudProviders.Bedrock, cloud), Deventry, key: null));
        Assert.Equal("Deventry has no Bedrock API key for Studio.", error.Message);
    }

    [Fact]
    public void Google_signs_in_with_gcloud_and_again_when_asked_or_with_a_key_file()
    {
        var gcloud = Filled(CloudProviders.Vertex);
        gcloud.RefreshSignIn = true;
        var keyFile = Filled(CloudProviders.AnthropicGoogleCloud);
        keyFile.SignIn = CloudSignIns.KeyFile;
        keyFile.KeyFile = "/home/me/sa.json";

        var withGcloud = AccountLaunch.For(Account(CloudProviders.Vertex, gcloud), Deventry);
        var withFile = AccountLaunch.For(Account(CloudProviders.AnthropicGoogleCloud, keyFile), Deventry);

        Assert.Equal("gcloud auth application-default login", Settings(withGcloud)["gcpAuthRefresh"]!.GetValue<string>());
        Assert.Null(Value(Env(withGcloud), "GOOGLE_APPLICATION_CREDENTIALS"));
        Assert.Equal("/home/me/sa.json", Value(Env(withFile), "GOOGLE_APPLICATION_CREDENTIALS"));
        Assert.Null(Settings(withFile)["gcpAuthRefresh"]);
    }

    [Fact]
    public void An_AWS_access_key_isnt_a_way_to_sign_in_Deventry_keeps_a_secret_for()
    {
        // Claude Code's awsCredentialExport takes only temporary credentials, so an access key goes in a profile.
        Assert.Equal([CloudSignIns.Profile, CloudSignIns.ApiKey], CloudProviders.Bedrock.SignIns);
        Assert.Equal([CloudSignIns.Profile, CloudSignIns.ApiKey], CloudProviders.AnthropicAws.SignIns);
        Assert.False(CloudSignIns.KeepsSecret(CloudSignIns.Profile));
    }

    [Fact]
    public void A_kind_or_sign_in_this_Deventry_doesnt_know_isnt_started()
    {
        var later = new ClaudeAccount { Id = Id, Name = "Later", Kind = "somethingNew", Cloud = new CloudAccountSettings() };
        var strange = Account(CloudProviders.Bedrock, new CloudAccountSettings { SignIn = "carrierPigeon", Region = "us-east-1" });

        Assert.Throws<InvalidOperationException>(() => AccountLaunch.For(later, Deventry));
        Assert.Throws<InvalidOperationException>(() => AccountLaunch.For(strange, Deventry));
    }

    // ---- Checking what an account says ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("us-east-1", true)]
    [InlineData("global", true)]
    [InlineData("eu", true)]
    [InlineData("us-east5", true)]
    [InlineData("US-EAST-1", false)]
    [InlineData("us east 1", false)]
    [InlineData("bedrock.us-east-1.amazonaws.com", false)]
    [InlineData("", false)]
    public void A_region_is_a_name_Claude_Code_takes(string region, bool fine) => Assert.Equal(fine, CloudProviders.IsRegion(region));

    [Fact]
    public void Settings_says_what_a_cloud_account_is_missing_or_has_wrong()
    {
        CloudAccountSettings With(CloudProvider provider, Action<CloudAccountSettings> change)
        {
            var cloud = Filled(provider);
            change(cloud);
            return cloud;
        }

        Assert.Null(CloudProviders.Problem(CloudProviders.Bedrock, Filled(CloudProviders.Bedrock)));
        Assert.Equal("Choose how Microsoft Foundry signs in.", CloudProviders.Problem(CloudProviders.Foundry, With(CloudProviders.Foundry, c => c.SignIn = CloudSignIns.Gcloud)));
        Assert.Equal("Fill in the region.", CloudProviders.Problem(CloudProviders.Bedrock, With(CloudProviders.Bedrock, c => c.Region = "")));
        Assert.Equal("Fill in the workspace ID.", CloudProviders.Problem(CloudProviders.AnthropicAws, With(CloudProviders.AnthropicAws, c => c.Workspace = null)));
        Assert.StartsWith("The region is a name such as us-east-1", CloudProviders.Problem(CloudProviders.Bedrock, With(CloudProviders.Bedrock, c => c.Region = "us-east-1.")), StringComparison.Ordinal);
        Assert.StartsWith("The resource name is the name alone", CloudProviders.Problem(CloudProviders.Foundry, With(CloudProviders.Foundry, c => c.Resource = "https://x.services.ai.azure.com")), StringComparison.Ordinal);
        Assert.Equal("The project ID has a space in it.", CloudProviders.Problem(CloudProviders.Vertex, With(CloudProviders.Vertex, c => c.Project = "my project")));
        Assert.StartsWith("The AWS profile's name", CloudProviders.Problem(CloudProviders.Bedrock, With(CloudProviders.Bedrock, c => c.Profile = "work; rm -rf ~")), StringComparison.Ordinal);
        Assert.Equal("Choose the service account key file.", CloudProviders.Problem(CloudProviders.Vertex, With(CloudProviders.Vertex, c => c.SignIn = CloudSignIns.KeyFile)));
        Assert.Equal("A model has a space in it.", CloudProviders.Problem(CloudProviders.Bedrock, With(CloudProviders.Bedrock, c => c.OpusModel = "claude opus")));
    }

    [Fact]
    public void Claude_Codes_provider_is_the_one_an_account_expects()
    {
        var bedrock = Account(CloudProviders.Bedrock, Filled(CloudProviders.Bedrock));
        var key = new ClaudeAccount { Id = Id, Name = "Work" };

        Assert.True(CloudProviders.IsExpected(bedrock, "bedrock"));
        Assert.True(CloudProviders.IsExpected(bedrock, "mantle"));
        Assert.True(CloudProviders.IsExpected(bedrock, null));
        Assert.False(CloudProviders.IsExpected(bedrock, "firstParty"));
        Assert.False(CloudProviders.IsExpected(bedrock, "gateway"));
        Assert.True(CloudProviders.IsExpected(key, "firstParty"));
        Assert.False(CloudProviders.IsExpected(key, "vertex"));
        Assert.Equal("a Claude apps gateway", CloudProviders.NameOf("gateway"));
        Assert.Equal("Google Cloud's Agent Platform", CloudProviders.NameOf("vertex"));
    }

    // ---- The account in the state ------------------------------------------------------------------------------------------

    [Fact]
    public void A_cloud_account_is_found_only_with_its_details_and_a_kind_this_Deventry_knows()
    {
        var state = new AppState
        {
            Accounts =
            [
                Account(CloudProviders.Foundry, Filled(CloudProviders.Foundry)),
                new ClaudeAccount { Id = "bare", Kind = AccountKinds.Bedrock },
                new ClaudeAccount { Id = "later", Kind = "somethingNew", Cloud = new CloudAccountSettings() },
            ],
        };

        Assert.Same(state.Accounts[0], AccountState.Find(state, Id));
        Assert.Null(AccountState.Find(state, "bare"));
        Assert.Null(AccountState.Find(state, "later"));
        Assert.Equal("Microsoft Foundry", state.Accounts[0].KindName);
        Assert.Equal("API key", new ClaudeAccount().KindName);
    }

    [Fact]
    public void Only_a_sign_in_with_a_key_keeps_a_secret_and_the_OS_says_what_it_is()
    {
        var profile = Account(CloudProviders.Bedrock, Filled(CloudProviders.Bedrock));
        var bedrockKey = Account(CloudProviders.Bedrock, new CloudAccountSettings { SignIn = CloudSignIns.ApiKey, Region = "us-east-1" });
        var foundryKey = Account(CloudProviders.Foundry, new CloudAccountSettings { SignIn = CloudSignIns.ApiKey, Resource = "r" });

        Assert.False(profile.HasSecret);
        Assert.True(bedrockKey.HasSecret);
        Assert.True(new ClaudeAccount().HasSecret);
        Assert.Equal("Deventry: Bedrock API key for Amazon Bedrock (Studio)", AccountSecrets.CredentialLabel(bedrockKey));
        Assert.Equal("Deventry: Foundry API key for Microsoft Foundry (Studio)", AccountSecrets.CredentialLabel(foundryKey));
        Assert.Equal("Deventry: Claude API key (Work)", AccountSecrets.CredentialLabel(new ClaudeAccount { Name = "Work" }));
        Assert.Equal($"claude-api-key/{Id}", AccountSecrets.CredentialKey(Id));
    }

    [Fact]
    public void A_settings_file_that_sets_the_key_variable_is_found_since_it_takes_over_from_the_environment()
    {
        var folder = Directory.CreateTempSubdirectory("deventry-env-").FullName;
        try
        {
            var user = Path.Combine(folder, "settings.json");
            var project = Path.Combine(folder, "project.json");
            File.WriteAllText(user, """{ "env": { "ANTHROPIC_FOUNDRY_API_KEY": "old" } }""");
            File.WriteAllText(project, """{ "env": { "ANTHROPIC_FOUNDRY_API_KEY": "" } }""");
            ClaudeSettingsFile[] files = [new(project, SettingsScope.Project), new(Path.Combine(folder, "missing.json"), SettingsScope.Local), new(user, SettingsScope.User)];

            Assert.Equal(user, AccountLaunch.SettingsFileSetting(files, "ANTHROPIC_FOUNDRY_API_KEY"));
            Assert.Null(AccountLaunch.SettingsFileSetting(files, "AWS_BEARER_TOKEN_BEDROCK"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
