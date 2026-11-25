using System.Net;
using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Accounts;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>A tab on an Ollama or llama.cpp account (DESIGN.md §18, "Ollama and llama.cpp accounts").</summary>
public class LocalAccountTabTests
{
    private static async Task<ClaudeAccount> AddOllamaAsync(TabTestHarness h, Action<LocalServerSettings>? change = null)
    {
        var local = new LocalServerSettings { Address = FakeLocalServers.OllamaAddress, Model = "qwen3:8b", Window = 4_096 * 10, StartServer = false };
        change?.Invoke(local);
        var account = await h.Services.Accounts.AddLocalAccountAsync("Home", LocalServers.Ollama, local, null);
        await TabTestHarness.Eventually(() => h.Services.Accounts.All.Count == 1, "the account");
        h.Services.Accounts.NewFolderAccount = account.Id;
        return account;
    }

    private static NoteItem[] Notes(TabViewModel tab) => InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().ToArray());

    [Fact]
    public async Task A_tab_starts_on_the_accounts_model_without_effort_a_fallback_or_WebSearch()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(settings => { settings.NewTabs.DefaultEffort = "high"; settings.ClaudeCode.FallbackModel = "sonnet"; }, http: ollama);
        var home = await AddOllamaAsync(h);

        var tab = await h.OpenTabAsync();

        Assert.Same(home, tab.Account);
        Assert.True(tab.IsOnLocalModel);
        var launch = h.Factory.Launches.Single();
        Assert.Equal("qwen3:8b", launch.Model);
        Assert.Null(launch.Effort);
        Assert.Null(launch.FallbackModel);
        Assert.Equal(["WebSearch"], launch.DisallowedTools);
        var env = JsonNode.Parse(launch.Settings!)!["env"]!;
        Assert.Equal(FakeLocalServers.OllamaAddress, env["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        Assert.Equal("40960", env["CLAUDE_CODE_MAX_CONTEXT_TOKENS"]!.GetValue<string>());
        Assert.Empty(tab.EffortLevels);
        Assert.Null(tab.PromptCacheLifetime);
        Assert.Contains(tab.InfoRows, r => r.Label == "Account" && r.Value == "Home (Ollama)");
        Assert.Equal("Home uses Ollama, and the Claude app needs a claude.ai sign-in", tab.RemoteControl.UnavailableReason);
    }

    [Fact]
    public async Task A_server_that_runs_the_model_at_a_smaller_window_than_the_accounts_is_said()
    {
        // Ollama on a 16 GB GPU: 4,096 tokens.
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        await AddOllamaAsync(h);

        var tab = await h.OpenTabAsync();

        Assert.Contains(Notes(tab), n => n.Kind == NoteKind.Warning
            && n.Text.StartsWith("Ollama runs qwen3:8b with a 4,096-token window here, but Home says 40,960. Ollama cuts the start off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_server_that_isnt_answering_stops_the_tab_with_why()
    {
        await using var h = new TabTestHarness();
        await AddOllamaAsync(h);

        await h.Shell.OpenFolderAsync(h.WorkFolder);
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Error, "the tab to fail to start");

        Assert.Empty(h.Factory.Launches);
        Assert.Contains(Notes(tab), n => n.Text == "Couldn't start Claude Code: Ollama isn't answering at localhost:11434. Start Ollama, then try again, or move the tab to another account.");
    }

    [Fact]
    public async Task A_model_the_server_hasnt_stops_the_tab_with_how_to_get_it()
    {
        using var ollama = FakeLocalServers.Ollama();
        ollama.On($"{FakeLocalServers.OllamaAddress}/api/show", _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"model 'gemma4' not found"}""") });
        await using var h = new TabTestHarness(http: ollama);
        await AddOllamaAsync(h, l => l.Model = "gemma4");

        await h.Shell.OpenFolderAsync(h.WorkFolder);
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Error, "the tab to fail to start");

        Assert.Contains(Notes(tab), n => n.Text.StartsWith("Couldn't start Claude Code: Ollama has no model called gemma4. Pull it with \"ollama pull gemma4\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deventry_starts_the_server_when_the_account_says_to_and_stops_it_when_it_closes()
    {
        var answering = false;
        using var ollama = FakeLocalServers.Ollama();
        ollama.On($"{FakeLocalServers.OllamaAddress}/api/version", _ => answering
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"version":"0.40.18"}""") }
            : throw new HttpRequestException("No connection could be made because the target machine actively refused it."));
        var launcher = new FakeLauncher();
        var program = Path.Combine(Path.GetTempPath(), "Ollama", OperatingSystem.IsWindows() ? "ollama.exe" : "ollama");
        launcher.OnStart = (spec, _) => answering |= spec.FileName == program;
        var h = new TabTestHarness(http: ollama, launcher: launcher);
        FakeProcess server;
        try
        {
            h.Services.LocalServers.FileExists = path => path == program;
            await AddOllamaAsync(h, l => { l.StartServer = true; l.ServerPath = program; l.Window = 65_536; });

            var tab = await h.OpenTabAsync();

            var started = Assert.Single(launcher.Started, s => s.FileName == program);
            Assert.Equal(["serve"], started.Arguments);
            Assert.Equal("127.0.0.1:11434", started.Environment!["OLLAMA_HOST"]);
            Assert.Equal("65536", started.Environment["OLLAMA_CONTEXT_LENGTH"]);
            Assert.Contains(Notes(tab), n => n.Text == "Started Ollama for Home. Deventry stops it when it closes.");
            Assert.Equal(1, h.Services.LocalServers.StartedCount);
            server = launcher.ProcessFor(started);
            Assert.False(server.Killed);
        }
        finally
        {
            await h.DisposeAsync();
        }

        Assert.True(server.Killed);
    }

    [Fact]
    public async Task A_turns_tokens_are_recorded_and_nothing_is_spent()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        using var spend = new AccountSpendViewModel(h.Services, tracker);
        var home = await AddOllamaAsync(h);
        var tab = await h.OpenTabAsync();
        spend.Show(tab.Account);

        h.Transport.EmitTurn(model: "qwen3:8b");
        await TabTestHarness.Eventually(() => tracker.Store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count > 0, "the turn's record");

        var turn = Assert.Single(tracker.Store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(home.Id, turn.Account);
        Assert.Equal(120, turn.Total);
        Assert.Equal(0, turn.CostUsd);
        Assert.Equal(0, tracker.Store.GetSpend(home.Id, DateTimeOffset.MinValue));
        // The header names the model and where it runs, with no spend.
        Assert.True(spend.IsShown);
        Assert.False(spend.HasSpend);
        Assert.Equal("Ollama · qwen3:8b · on this computer", spend.KindText);
        Assert.Equal("The model's server", spend.AccessibleName);
        Assert.StartsWith("Claude Code runs on qwen3:8b, which Ollama serves on this computer: nothing is spent.", spend.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Images_dont_go_to_a_model_that_cant_see_them()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        await AddOllamaAsync(h);
        var tab = await h.OpenTabAsync();
        Assert.True(tab.AddImage(TestImages.Png, "screenshot"));
        tab.ComposerText = "What's in this?";

        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));

        Assert.Contains(Notes(tab), n => n.Text == "qwen3:8b can't see images, so Ollama would turn this message down. Remove the image, or choose a model that can see in Settings → Agents → Claude Code → Accounts.");
        Assert.Single(tab.Attachments);
        Assert.Equal("What's in this?", tab.ComposerText);
        Assert.DoesNotContain(h.Transport.SentUserTexts, text => text.Contains("What's in this?", StringComparison.Ordinal));
    }

    // ---- Moving a tab onto one --------------------------------------------------------------------------------------------

    private static async Task<TabViewModel> DefaultTabWithAConversationAsync(TabTestHarness h, long tokens)
    {
        h.Transport.Answers["get_context_usage"] = _ => new JsonObject { ["totalTokens"] = tokens, ["maxTokens"] = 1_000_000, ["percentage"] = 2.0 };
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.SessionId == "s1" && tab.Context.Tokens == tokens && tab.Status == TabStatus.Idle && tab.IsSettled, "the turn");
        return tab;
    }

    [Fact]
    public async Task Moving_a_tab_to_a_local_account_says_its_free()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        var tab = await DefaultTabWithAConversationAsync(h, 18_000);
        var home = await AddOllamaAsync(h, l => l.Window = 65_536);
        h.Services.Accounts.NewFolderAccount = null;

        tab.SwitchAccountCommand.Execute(home.Id);

        var confirm = h.Shell.Confirmation!;
        Assert.Equal("Move", confirm.ConfirmText);
        Assert.Equal("qwen3:8b reads the conversation's 18,000 tokens again on Ollama, on this computer: it's free, and takes a moment. Its window is 65,536 tokens.", confirm.Message);
        await confirm.ConfirmCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.IsSettled, "the restart on Home");
        Assert.Same(home, tab.Account);
        Assert.Equal("s1", h.Factory.Launches[1].Resume);
        Assert.Equal("qwen3:8b", h.Factory.Launches[1].Model);
    }

    [Fact]
    public async Task A_conversation_too_big_for_the_models_window_starts_a_new_chat_with_a_digest_instead()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        var tab = await DefaultTabWithAConversationAsync(h, 150_000);
        var home = await AddOllamaAsync(h, l => l.Window = 65_536);
        h.Services.Accounts.NewFolderAccount = null;

        tab.SwitchAccountCommand.Execute(home.Id);

        var confirm = h.Shell.Confirmation!;
        Assert.Equal("New chat with a digest", confirm.ConfirmText);
        Assert.StartsWith("The conversation's 150,000 tokens don't fit qwen3:8b's 65,536-token window, so it can't move to Home as it is.", confirm.Message, StringComparison.Ordinal);
        await confirm.ConfirmCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.IsSettled, "the new chat on Home");
        Assert.Same(home, tab.Account);
        Assert.Null(h.Factory.Launches[1].Resume);
        Assert.StartsWith("Context from an earlier conversation (a digest):", tab.ComposerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_stopped_answering_mid_session_is_said_and_checked_before_the_next_message()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        await AddOllamaAsync(h);
        var tab = await h.OpenTabAsync();
        // As Claude Code ends a turn after its retries, when the server has gone.
        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = "qwen3:8b", ["permissionMode"] = "default" });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "result", ["subtype"] = "success", ["is_error"] = true, ["terminal_reason"] = "api_error", ["session_id"] = "s1",
            ["result"] = "API Error: Connection refused — a firewall or proxy may be blocking it (ECONNREFUSED)",
        });
        await TabTestHarness.Eventually(() => Notes(tab).Any(n => n.Text.StartsWith("Ollama stopped answering", StringComparison.Ordinal)), "the note");
        Assert.Contains(Notes(tab), n => n.Text == "Ollama stopped answering at localhost:11434. Start it again, then send your message.");

        // Still down: the message waits.
        ollama.On($"{FakeLocalServers.OllamaAddress}/api/version", _ => throw new HttpRequestException("refused"));
        tab.ComposerText = "hello again";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));

        Assert.Equal("hello again", tab.ComposerText);
        Assert.Contains(Notes(tab), n => n.Text == "Ollama isn't answering at localhost:11434. Start Ollama, then try again, or move the tab to another account.");
    }
}
