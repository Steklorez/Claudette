using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.Accounts;

namespace Deventry.App.Tests;

/// <summary>Ollama and llama.cpp accounts in Settings → Agents → Claude Code → Accounts (DESIGN.md §18).</summary>
public class LocalAccountsTests
{
    private const string Ollama = FakeLocalServers.OllamaAddress;
    private const string Llama = FakeLocalServers.LlamaAddress;

    [Fact]
    public async Task Settings_offers_Ollama_and_llama_cpp_and_says_what_an_account_of_each_is()
    {
        await using var h = new TabTestHarness();
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);

        Assert.Equal(["Ollama", "llama.cpp"], page.KindChoices.TakeLast(2).Select(c => c.Label));
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);

        var form = page.NewLocal!;
        Assert.False(page.IsNewApiKey);
        Assert.Null(page.NewCloud);
        Assert.Equal("http://localhost:11434", form.Address);
        Assert.Contains("Claude Code runs as it always does, but its model runs in Ollama", form.Explanation, StringComparison.Ordinal);
        Assert.Contains("Anthropic doesn't support Claude Code on models other than Claude", form.Explanation, StringComparison.Ordinal);
        Assert.True(form is { StartServer: true, CanStartServer: true, TakesKey: false, Window: LocalServers.DefaultWindow, LongestReply: LocalServers.DefaultLongestReply });

        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.LlamaCpp);
        Assert.Equal("http://localhost:9931", page.NewLocal!.Address);
        Assert.True(page.NewLocal.IsLlamaCpp);
    }

    [Fact]
    public async Task Check_lists_the_servers_models_and_choosing_one_fills_in_its_name_and_window()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);
        var form = page.NewLocal!;

        await form.CheckCommand.ExecuteAsync(null);

        Assert.Equal("Ollama 0.40.18: 2 models, 1 of them can use tools.", form.CheckText);
        Assert.Equal(["qwen3:8b", "llava:7b"], form.ModelChoices.Select(c => c.Name));
        Assert.Contains("40,960 tokens · tools, thinking", form.ModelChoices[0].Detail, StringComparison.Ordinal);
        Assert.EndsWith("can't use tools, so Claude Code can't run on it", form.ModelChoices[1].Detail, StringComparison.Ordinal);

        form.SelectedModel = form.ModelChoices[1];
        Assert.Contains("can't use tools", form.ModelNote, StringComparison.Ordinal);
        Assert.Equal("llava:7b can't use tools: choose another model.", form.Problem);

        form.SelectedModel = form.ModelChoices[0];
        Assert.Equal("qwen3:8b", form.Model);
        // As much as it was trained for, which is less than Deventry's default.
        Assert.Equal(40_960, form.Window);
        Assert.Null(form.Problem);
        Assert.StartsWith("Claude Code needs 64,000 tokens or more to work well", form.WindowNote, StringComparison.Ordinal);
        Assert.Contains("compacts the conversation at about 19,768 tokens", form.WindowNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_isnt_answering_says_so()
    {
        await using var h = new TabTestHarness();
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);

        await page.NewLocal!.CheckCommand.ExecuteAsync(null);

        Assert.True(page.NewLocal.CheckFailed);
        Assert.StartsWith("Ollama isn't answering at localhost:11434", page.NewLocal.CheckText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_window_smaller_on_the_server_than_the_accounts_is_said_and_Ollama_can_make_a_copy_with_the_accounts()
    {
        var created = new List<JsonNode>();
        using var ollama = FakeLocalServers.Ollama(created);
        await using var h = new TabTestHarness(http: ollama);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);
        var form = page.NewLocal!;
        await form.CheckCommand.ExecuteAsync(null);
        form.SelectedModel = form.ModelChoices[0];
        Assert.False(form.CanMakeCopy);

        await form.CheckWindowCommand.ExecuteAsync(null);

        Assert.Equal(4_096, form.ServerWindow);
        Assert.True(form.WindowIsSmall);
        Assert.StartsWith("Ollama runs qwen3:8b with a 4,096-token window here, not 40,960. Ollama would cut the start off each request", form.WindowNote, StringComparison.Ordinal);
        Assert.True(form.CanMakeCopy);
        Assert.Equal("Make a copy with a 40,960-token window…", form.CopyText);
        Assert.StartsWith("Ollama makes qwen3:8b-40k from qwen3:8b", form.CopyQuestion, StringComparison.Ordinal);

        // It asks first.
        form.MakeCopy.Ask();
        Assert.Empty(created);
        await form.MakeCopy.ConfirmCommand.ExecuteAsync(null);

        var asked = Assert.Single(created);
        Assert.Equal("qwen3:8b", asked["from"]!.GetValue<string>());
        Assert.Equal(40_960, asked["parameters"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal("qwen3:8b-40k", form.Model);
        Assert.Equal(40_960, form.ServerWindow);
        Assert.False(form.CanMakeCopy);
        Assert.Equal("Made qwen3:8b-40k, which runs at 40,960 tokens.", form.CheckText);
    }

    [Fact]
    public async Task Adding_an_Ollama_account_keeps_no_key_and_its_editor_has_no_spend()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(http: ollama);
        var keys = UseKeys(h);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewName = "Home";
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);
        var form = page.NewLocal!;
        await form.CheckCommand.ExecuteAsync(null);
        form.SelectedModel = form.ModelChoices[0];
        form.KeepOffline = true;

        await page.AddCommand.ExecuteAsync(null);

        var editor = Assert.Single(page.Editors);
        Assert.Equal("Ollama · qwen3:8b", editor.KindText);
        Assert.False(editor.HasSpend);
        Assert.False(editor.HasKey);
        Assert.True(editor.HasDetails);
        var local = editor.Account.Local!;
        Assert.True(local is { Address: Ollama, Model: "qwen3:8b", Window: 40_960, KeepOffline: true, StartServer: true, HasKey: false });
        Assert.Empty(keys.Secrets);
        Assert.NotSame(form, page.NewLocal);
        Assert.StartsWith("Added Home.", page.AddStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_llama_servers_key_goes_only_to_the_credential_store_and_asks_the_server_with_it()
    {
        using var llama = FakeLocalServers.Llama("llama-key");
        await using var h = new TabTestHarness(http: llama);
        var keys = UseKeys(h);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewName = "GPU box";
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.LlamaCpp);
        var form = page.NewLocal!;
        form.StartServer = false;

        await form.CheckCommand.ExecuteAsync(null);
        Assert.Equal("llama-server at localhost:9931 wants an API key.", form.CheckText);

        form.TakesKey = true;
        await page.AddCommand.ExecuteAsync(null);
        Assert.Equal("Choose the model.", page.AddStatus);
        form.ApiKey = " llama-key ";
        await form.CheckCommand.ExecuteAsync(null);
        Assert.Equal("llama-server b12723: 1 model.", form.CheckText);
        // Its only model, at the window it was started with.
        Assert.Equal("qwen3-8b", form.Model);
        Assert.Equal(65_536, form.Window);

        await page.AddCommand.ExecuteAsync(null);

        var account = Assert.Single(h.Services.Accounts.All);
        Assert.Equal("llama-key", keys.Secrets[AccountSecrets.CredentialKey(account.Id)]);
        Assert.True(account.Local!.HasKey);
        Assert.DoesNotContain("llama-key", JsonSerializer.Serialize(h.Services.State), StringComparison.Ordinal);
        Assert.Equal("", form.ApiKey);
    }

    [Fact]
    public async Task Changing_an_accounts_settings_uses_its_kept_key_and_a_key_it_no_longer_takes_is_removed()
    {
        using var llama = FakeLocalServers.Llama("llama-key");
        await using var h = new TabTestHarness(http: llama);
        var keys = UseKeys(h);
        var account = await h.Services.Accounts.AddLocalAccountAsync("GPU box", LocalServers.LlamaCpp,
            new LocalServerSettings { Address = Llama, Model = "qwen3-8b", HasKey = true, StartServer = false }, "llama-key");
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        var editor = Assert.Single(page.Editors);

        editor.EditDetailsCommand.Execute(null);
        var form = editor.LocalDetails!;
        Assert.False(form.IsNew);
        Assert.StartsWith("Kept in", form.KeyPlaceholder, StringComparison.Ordinal);
        // No key typed: the kept one asks the server.
        await form.CheckCommand.ExecuteAsync(null);
        Assert.Equal("llama-server b12723: 1 model.", form.CheckText);

        form.LongestReply = 4_096;
        await editor.SaveDetailsCommand.ExecuteAsync(null);
        Assert.Equal(4_096, account.Local!.LongestReply);
        Assert.Equal("llama-key", keys.Secrets[AccountSecrets.CredentialKey(account.Id)]);
        Assert.Equal("Saved. Tabs on GPU box use the change when they start again.", editor.Status);

        editor.EditDetailsCommand.Execute(null);
        editor.LocalDetails!.TakesKey = false;
        await editor.SaveDetailsCommand.ExecuteAsync(null);

        Assert.False(account.Local!.HasKey);
        Assert.Empty(keys.Secrets);
    }

    [Fact]
    public async Task Llama_server_started_by_Deventry_needs_a_model_to_load_which_names_the_accounts_model()
    {
        await using var h = new TabTestHarness();
        UseKeys(h);
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewName = "Laptop";
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.LlamaCpp);
        var form = page.NewLocal!;
        form.Model = "qwen3";

        await page.AddCommand.ExecuteAsync(null);
        Assert.StartsWith("Choose the model llama-server loads when Deventry starts it", page.AddStatus, StringComparison.Ordinal);

        form.Model = "";
        form.ServerModel = Path.Combine(h.Root, "Qwen3-8B-Q4_K_M.gguf");
        Assert.Equal("Qwen3-8B-Q4_K_M", form.Model);
        await page.AddCommand.ExecuteAsync(null);

        var account = Assert.Single(h.Services.Accounts.All);
        Assert.Equal(Path.Combine(h.Root, "Qwen3-8B-Q4_K_M.gguf"), account.Local!.ServerModel);
    }

    [Fact]
    public async Task A_server_elsewhere_cant_be_started_by_Deventry()
    {
        await using var h = new TabTestHarness();
        using var page = new ClaudeCodePage(new SettingsContext(h.Services), null, null);
        page.NewKind = page.KindChoices.Single(c => c.Server == LocalServers.Ollama);
        var form = page.NewLocal!;

        form.Address = "http://gpu-box.local:11434";

        Assert.False(form.CanStartServer);
        Assert.Equal("Deventry can start Ollama only on this computer.", form.StartNote);
        Assert.False(form.ToSettings().StartServer);
    }

    private static FakeCredentialStore UseKeys(TabTestHarness h)
    {
        var keys = new FakeCredentialStore();
        h.Services.Accounts.Credentials = keys;
        h.Services.Accounts.HelperExecutable = Path.Combine(h.Root, "Deventry.exe");
        return keys;
    }
}
