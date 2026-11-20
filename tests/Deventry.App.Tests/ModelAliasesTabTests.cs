using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Auth;

namespace Deventry.App.Tests;

/// <summary>
/// Signed in with a token from <c>CLAUDE_CODE_OAUTH_TOKEN</c>, Claude Code offers Fable only as an older pinned model:
/// the picker adds the family's newest, and the account says why (DESIGN.md §5, "Model &amp; effort"; §11, "Detecting").
/// </summary>
public class ModelAliasesTabTests
{
    /// <summary>Claude Code 2.1.328's <c>initialize</c> reply signed in with <c>claude setup-token</c>'s token: its models and account.</summary>
    private const string TokenSignInReply = """
        {
          "models": [
            {"value":"default","resolvedModel":"claude-sonnet-5-5","displayName":"Default (recommended)","description":"Sonnet 5.5 · Efficient for routine tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"sonnet","resolvedModel":"claude-sonnet-5-5","displayName":"Sonnet","description":"Sonnet 5.5 · Efficient for routine tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"claude-fable-5-1[1m]","resolvedModel":"claude-fable-5-1","displayName":"Fable","description":"Fable 5.1 · Most capable for your hardest and longest-running tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high","max"],"supportsAutoMode":true},
            {"value":"opus","resolvedModel":"claude-opus-5-5","displayName":"Opus","description":"Opus 5.5 · Best for everyday, complex tasks","supportsEffort":true,"supportedEffortLevels":["low","medium","high"],"supportsAutoMode":true},
            {"value":"haiku","resolvedModel":"claude-haiku-5-5","displayName":"Haiku","description":"Haiku 5.5 · Fastest for quick answers"}
          ],
          "account": {"tokenSource":"CLAUDE_CODE_OAUTH_TOKEN","apiProvider":"firstParty"},
          "current_permission_mode": "default"
        }
        """;

    private static readonly AuthStatus TokenSignIn = new(true, "oauth_token", "firstParty", null, null, null, null, null);

    private static void AnswerAsTokenSignIn(TabTestHarness h) =>
        h.Transport.Answers["initialize"] = _ => JsonNode.Parse(TokenSignInReply)!.AsObject();

    [Fact]
    public async Task The_picker_offers_the_newest_Fable_and_choosing_it_sends_the_alias()
    {
        await using var h = new TabTestHarness();
        AnswerAsTokenSignIn(h);
        var tab = await h.OpenTabAsync();

        Assert.Equal(["sonnet", "fable", "claude-fable-5-1[1m]", "opus", "haiku"], tab.Models.Select(m => m.Value));
        var newest = tab.Models.Single(m => m.Value == "fable");
        Assert.Equal("Fable (newest)", newest.DisplayName);
        Assert.Contains(h.Services.KnownModels(tab.Agent), m => m.Value == "fable");

        tab.ChooseModelCommand.Execute(newest);
        await tab.ModelSwitch.ConfirmCommand.ExecuteAsync(null);

        var request = h.Transport.Sent.Last(m => m["request"]?["subtype"]?.GetValue<string>() == "set_model");
        Assert.Equal("fable", request["request"]!["model"]!.GetValue<string>());
        Assert.Equal("fable", tab.State.Overrides.Model);
        Assert.Equal("Fable (newest)", tab.ModelName);
        Assert.Equal(["low", "medium", "high", "max"], tab.EffortLevels.Select(e => e.Value));

        // The next turn says which model the alias runs, which Claude Code didn't list.
        h.Transport.Emit("""{"type":"system","subtype":"init","session_id":"s1","model":"claude-fable-5-2","permissionMode":"default"}""");
        await TabTestHarness.Eventually(() => tab.ModelName == "Fable 5.2", "the model the alias runs");
        Assert.Equal(["low", "medium", "high", "max"], tab.EffortLevels.Select(e => e.Value));
    }

    [Fact]
    public async Task Any_other_sign_in_gets_the_list_as_Claude_Code_sent_it()
    {
        await using var h = new TabTestHarness();
        // An API key's, or a gateway's: Fable only pinned may be what it serves.
        h.Transport.Answers["initialize"] = _ =>
        {
            var reply = JsonNode.Parse(TokenSignInReply)!.AsObject();
            reply["account"] = new JsonObject { ["apiProvider"] = "firstParty" };
            return reply;
        };
        var tab = await h.OpenTabAsync();

        Assert.Equal(["sonnet", "claude-fable-5-1[1m]", "opus", "haiku"], tab.Models.Select(m => m.Value));
        await TabTestHarness.Eventually(() => h.Services.ClaudeSignInReported, "the tab's sign-in");
        Assert.Null(h.Services.ClaudeEnvironmentToken);
    }

    [Fact]
    public async Task The_account_menu_Settings_and_Diagnostics_say_the_token_cant_see_the_plan()
    {
        await using var h = new TabTestHarness();
        AnswerAsTokenSignIn(h);
        var account = new AccountViewModel(h.Services) { Status = TokenSignIn };
        Assert.False(account.HasTokenNote);
        var notified = new List<string?>();
        account.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        h.Services.ClaudeEnvironmentTokenChanged += account.OnEnvironmentTokenChanged;

        await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => account.HasTokenNote, "the note");

        const string Note = "Signed in with a token from CLAUDE_CODE_OAUTH_TOKEN. Claude Code can't see your plan this way, so its list of models can lag behind; Deventry adds each family's newest model to the picker.";
        Assert.Equal(Note, account.TokenNote);
        Assert.Contains(nameof(AccountViewModel.TokenNote), notified);
        var settings = new SettingsViewModel(h.Services, null, null) { SelectedCategory = "Advanced" };
        Assert.Equal(Note, settings.Advanced.ClaudeSignInText);
        Assert.Contains($"Claude Code sign-in: {Note}", settings.Advanced.DiagnosticsReport(includeHeader: true), StringComparison.Ordinal);

        account.Status = new AuthStatus(false, "none", "firstParty", null, null, null, null, null);
        Assert.Null(account.TokenNote);
    }
}
