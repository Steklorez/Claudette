using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.Accounts;

namespace Deventry.App.Tests;

/// <summary>Cloud provider accounts: adding one, what its tabs start with, and what they say (DESIGN.md §4, "Accounts").</summary>
public class CloudAccountsTests
{
    private const string Init = """{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5","permissionMode":"default"}""";
    private const string SignedOutReply = """{"type":"assistant","message":{"model":"<synthetic>","content":[{"type":"text","text":"Invalid API key · Fix external API key"}]},"error":"authentication_failed"}""";

    private static readonly CloudAccountSettings BedrockProfile = new() { SignIn = CloudSignIns.Profile, Region = "us-east-1", Profile = "studio-sso", RefreshSignIn = true };

    // ---- What a tab starts with ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cloud_accounts_tab_starts_on_its_provider_with_nothing_secret_on_the_command_line()
    {
        await using var h = new TabTestHarness();
        var keys = UseKeys(h);
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Bedrock, BedrockProfile, secret: null);
        h.Services.Accounts.NewFolderAccount = studio.Id;

        var tab = await h.OpenTabAsync();

        Assert.Same(studio, tab.Account);
        Assert.Empty(keys.Secrets);
        var launch = h.Factory.Launches.Single();
        var settings = JsonNode.Parse(launch.Settings!)!.AsObject();
        Assert.Equal("1", settings["env"]!["CLAUDE_CODE_USE_BEDROCK"]!.GetValue<string>());
        Assert.Equal("us-east-1", settings["env"]!["AWS_REGION"]!.GetValue<string>());
        Assert.Equal("studio-sso", settings["env"]!["AWS_PROFILE"]!.GetValue<string>());
        Assert.Equal("aws sso login --profile studio-sso", settings["awsAuthRefresh"]!.GetValue<string>());
        // The Default account's provider, wherever the user set it, is off for the tab.
        Assert.Equal("", settings["env"]!["CLAUDE_CODE_USE_VERTEX"]!.GetValue<string>());
        Assert.Null(launch.EnvironmentOverrides["CLAUDE_CODE_USE_VERTEX"]);
        Assert.Contains(tab.InfoRows, r => r.Label == "Account" && r.Value == "Studio (Amazon Bedrock)");
        Assert.Equal("Studio uses Amazon Bedrock, and the Claude app needs a claude.ai sign-in", tab.RemoteControl.UnavailableReason);
    }

    [Fact]
    public async Task A_providers_key_goes_only_in_its_tabs_environment_never_in_the_state_or_on_the_command_line()
    {
        await using var h = new TabTestHarness();
        var keys = UseKeys(h);
        var cloud = new CloudAccountSettings { SignIn = CloudSignIns.ApiKey, Resource = "studio-ai" };
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Foundry, cloud, " foundry-key ");
        h.Services.Accounts.NewFolderAccount = studio.Id;

        await h.OpenTabAsync();

        var launch = h.Factory.Launches.Single();
        Assert.Equal("foundry-key", keys.Secrets[AccountSecrets.CredentialKey(studio.Id)]);
        Assert.Equal("foundry-key", launch.EnvironmentOverrides["ANTHROPIC_FOUNDRY_API_KEY"]);
        Assert.DoesNotContain("foundry-key", launch.Settings, StringComparison.Ordinal);
        Assert.DoesNotContain("foundry-key", JsonSerializer.Serialize(h.Services.State), StringComparison.Ordinal);
        Assert.Equal("studio-ai", JsonNode.Parse(launch.Settings!)!["env"]!["ANTHROPIC_FOUNDRY_RESOURCE"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_key_that_a_settings_file_sets_over_the_accounts_is_said_to_take_over()
    {
        await using var h = new TabTestHarness();
        var keys = UseKeys(h);
        var config = Path.Combine(h.Root, "claude-config");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "settings.json"), """{ "env": { "ANTHROPIC_FOUNDRY_API_KEY": "an-old-key" } }""", TestContext.Current.CancellationToken);
        h.Services.ClaudeConfigDirectory = config;
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Foundry, new CloudAccountSettings { SignIn = CloudSignIns.ApiKey, Resource = "studio-ai" }, "foundry-key");
        h.Services.Accounts.NewFolderAccount = studio.Id;

        var tab = await h.OpenTabAsync();

        Assert.Contains(InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().ToArray()),
            n => n.Text == $"{Path.Combine(config, "settings.json")} sets ANTHROPIC_FOUNDRY_API_KEY, which Claude Code uses over Studio's key. Remove it there for this account's key to be used.");
    }

    [Fact]
    public async Task A_missing_key_stops_the_tab_and_says_why()
    {
        await using var h = new TabTestHarness();
        var keys = UseKeys(h);
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Bedrock, new CloudAccountSettings { SignIn = CloudSignIns.ApiKey, Region = "us-east-1" }, "bedrock-key");
        keys.Secrets.Clear();
        h.Services.Accounts.NewFolderAccount = studio.Id;

        await h.Shell.OpenFolderAsync(h.WorkFolder);
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Error, "the tab to fail to start");

        Assert.Empty(h.Factory.Launches);
        Assert.Contains(InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().ToArray()),
            n => n.Text == "Couldn't start Claude Code: Deventry has no Bedrock API key for Studio.");
    }

    [Fact]
    public async Task A_tab_Claude_Code_puts_on_another_provider_than_its_accounts_says_so()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        var initialize = h.Transport.Answers["initialize"];
        h.Transport.Answers["initialize"] = request =>
        {
            var answer = initialize(request)!;
            answer["account"] = new JsonObject { ["apiProvider"] = "firstParty" };
            return answer;
        };
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Vertex, new CloudAccountSettings { SignIn = CloudSignIns.Gcloud, Project = "studio", Region = "global" }, null);
        h.Services.Accounts.NewFolderAccount = studio.Id;

        var tab = await h.OpenTabAsync();

        Assert.Contains(InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().ToArray()),
            n => n.Text.StartsWith("Claude Code is using Anthropic's API for this tab, not Google Cloud's Agent Platform as Studio says.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refused_credentials_are_the_tabs_own_error_and_say_how_to_sign_in_again()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        var signInAsked = 0;
        h.OnAuthenticationRequired = () => signInAsked++;
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Bedrock, BedrockProfile, null);
        h.Services.Accounts.NewFolderAccount = studio.Id;
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Init);
        h.Transport.Emit(SignedOutReply);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Error, "the refused credentials");

        Assert.Equal(0, signInAsked);
        Assert.Contains(InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().ToArray()),
            n => n.Text == "Claude Code couldn't sign in to Amazon Bedrock for Studio. Sign in to AWS with \"aws sso login --profile studio-sso\", or check the profile in Settings → Claude Code → Accounts. Or move the tab to another account.");
    }

    [Fact]
    public async Task Moving_a_tab_to_a_cloud_account_says_its_billed_through_the_provider()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        h.Transport.Answers["get_context_usage"] = _ => new JsonObject { ["totalTokens"] = 150_000, ["maxTokens"] = 1_000_000, ["percentage"] = 15.0 };
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Bedrock, BedrockProfile, null);
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.SessionId == "s1" && tab.Context.Tokens == 150_000 && tab.Status == TabStatus.Idle && tab.IsSettled, "the turn");

        tab.SwitchAccountCommand.Execute(studio.Id);

        Assert.EndsWith("once: about $0.75 at Anthropic's list price, billed through Amazon Bedrock.", h.Shell.Confirmation!.Message, StringComparison.Ordinal);
    }

    // ---- Settings → Claude Code → Accounts -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Settings_adds_a_providers_key_only_once_told_what_the_key_is_exposed_to()
    {
        await using var h = new TabTestHarness();
        var keys = UseKeys(h);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        Assert.Equal(["Anthropic API key", "Amazon Bedrock", "Claude Platform on AWS", "Google Cloud's Agent Platform", "Claude Models on Google Cloud", "Microsoft Foundry"],
            page.KindChoices.Select(c => c.Label));

        page.NewName = "Studio";
        page.NewKind = page.KindChoices.Single(c => c.Provider == CloudProviders.Foundry);
        var form = page.NewCloud!;
        Assert.Equal(["Microsoft Entra ID (az login)", "Foundry API key"], form.SignIns.Select(s => s.Label));
        form.SignIn = form.SignIns[1];
        Assert.True(form.ExposesKey);
        Assert.Contains("Every command Claude runs there inherits it", form.ExposureWarning, StringComparison.Ordinal);
        Assert.Contains("sign in another way instead: Microsoft Entra ID (az login)", form.ExposureWarning, StringComparison.Ordinal);

        await page.AddCommand.ExecuteAsync(null);
        Assert.Equal("Fill in the resource name.", page.AddStatus);
        form.Resource = "studio-ai";
        await page.AddCommand.ExecuteAsync(null);
        Assert.Equal("Paste the Foundry API key.", page.AddStatus);
        form.ApiKey = "foundry-key";
        await page.AddCommand.ExecuteAsync(null);
        Assert.StartsWith("Read what putting the Foundry API key in the environment exposes it to", page.AddStatus, StringComparison.Ordinal);
        Assert.Empty(h.Services.Accounts.All);

        form.AcceptsExposure = true;
        await page.AddCommand.ExecuteAsync(null);

        var editor = Assert.Single(page.Editors);
        Assert.Equal(AccountKinds.Foundry, editor.Account.Kind);
        Assert.Equal("Microsoft Foundry · Foundry API key", editor.KindText);
        Assert.Equal("foundry-key", keys.Secrets[AccountSecrets.CredentialKey(editor.Account.Id)]);
        // The typed key is forgotten, and the next account starts from an empty form.
        Assert.Equal("", form.ApiKey);
        Assert.NotSame(form, page.NewCloud);
        Assert.True(editor.HasKey);
        Assert.True(editor.ExposesKey);
    }

    [Fact]
    public async Task Settings_changes_a_cloud_accounts_settings_but_not_to_something_wrong()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        var studio = await h.Services.Accounts.AddCloudAccountAsync("Studio", CloudProviders.Bedrock, BedrockProfile, null);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        var editor = Assert.Single(page.Editors);
        Assert.False(editor.HasKey);
        Assert.True(editor.IsCloud);

        editor.EditDetailsCommand.Execute(null);
        var details = editor.Details!;
        Assert.False(details.CanChooseSignIn);
        details.Region = "eu west";
        editor.SaveDetailsCommand.Execute(null);
        Assert.StartsWith("The region is a name such as us-east-1", editor.Status, StringComparison.Ordinal);
        Assert.Equal("us-east-1", studio.Cloud!.Region);

        details.Region = "eu-west-1";
        details.OpusModel = "eu.anthropic.claude-opus-4-8";
        editor.SaveDetailsCommand.Execute(null);

        Assert.False(editor.IsEditingDetails);
        Assert.Equal("eu-west-1", studio.Cloud!.Region);
        Assert.Equal("eu.anthropic.claude-opus-4-8", studio.Cloud.OpusModel);
        Assert.Equal("studio-sso", studio.Cloud.Profile);
        Assert.Equal("Saved. Tabs on Studio use the change when they start again.", editor.Status);
    }

    [Fact]
    public async Task Without_a_credential_store_an_account_that_needs_no_key_can_still_be_added_and_removed()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        h.Services.Accounts.Credentials = new Core.Credentials.UnavailableCredentialStore();
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        Assert.False(page.CanAdd);

        page.NewName = "Studio";
        page.NewKind = page.KindChoices.Single(c => c.Provider == CloudProviders.Vertex);
        Assert.True(page.CanAdd);
        page.NewCloud!.Project = "studio";
        page.NewCloud.Region = "global";
        page.NewCloud.SignIn = page.NewCloud.SignIns.Single(s => s.Value == CloudSignIns.KeyFile);
        page.NewCloud.KeyFile = Path.Combine(h.Root, "sa.json");
        await page.AddCommand.ExecuteAsync(null);

        var editor = Assert.Single(page.Editors);
        Assert.Equal(CloudSignIns.KeyFile, editor.Account.Cloud!.SignIn);
        Assert.False(editor.Account.Cloud.RefreshSignIn);

        editor.Removal.Ask();
        await editor.Removal.ConfirmCommand.ExecuteAsync(null);
        Assert.Empty(h.Services.Accounts.All);
    }

    private static FakeCredentialStore UseKeys(TabTestHarness h)
    {
        var keys = new FakeCredentialStore();
        h.Services.Accounts.Credentials = keys;
        h.Services.Accounts.HelperExecutable = HelperPath(h);
        return keys;
    }

    private static string HelperPath(TabTestHarness h) => Path.Combine(h.Root, "Deventry.exe");
}
