using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.Core.Tests.Accounts;

/// <summary>Ollama and llama.cpp accounts, and what a tab under one starts Claude Code with (DESIGN.md §18).</summary>
public class LocalAccountsTests
{
    private const string Id = "0a1b2c3d4e5f";
    private const string Deventry = "/opt/Deventry/Deventry";

    private static ClaudeAccount Account(LocalServerKind server, LocalServerSettings local) =>
        new() { Id = Id, Name = "Home", Kind = server.Kind, Local = local };

    private static LocalServerSettings Ollama(Action<LocalServerSettings>? change = null)
    {
        var local = new LocalServerSettings { Address = "http://localhost:11434", Model = "qwen3:8b", Window = 40_960 };
        change?.Invoke(local);
        return local;
    }

    private static JsonObject Settings(AccountLaunch launch) => JsonNode.Parse(launch.Settings)!.AsObject();

    private static JsonObject Env(AccountLaunch launch) => Settings(launch)["env"]!.AsObject();

    private static string? Value(JsonObject env, string variable) => env[variable]?.GetValue<string>();

    // ---- The account ---------------------------------------------------------------------------------------------------

    [Fact]
    public void An_Ollama_or_llama_cpp_account_is_a_local_one_tabs_can_run_under()
    {
        var ollama = Account(LocalServers.Ollama, Ollama());
        var llama = Account(LocalServers.LlamaCpp, new LocalServerSettings { Address = "http://localhost:9931", Model = "qwen3", HasKey = true });

        Assert.True(ollama.IsLocal);
        Assert.True(ollama.IsUsable);
        Assert.False(ollama.IsCloudProvider);
        Assert.Equal("Ollama", ollama.KindName);
        Assert.False(ollama.HasSecret);
        Assert.Equal("llama.cpp", llama.KindName);
        Assert.True(llama.HasSecret);
        Assert.Equal("Deventry: API key for llama-server (Home)", AccountSecrets.CredentialLabel(llama));
    }

    [Fact]
    public void An_account_of_a_local_kind_without_its_settings_cant_be_used()
    {
        var state = new AppState();
        state.Accounts.Add(new ClaudeAccount { Id = Id, Name = "Broken", Kind = AccountKinds.Ollama });
        var working = Account(LocalServers.LlamaCpp, Ollama());
        working.Id = "1b2c3d";
        state.Accounts.Add(working);

        Assert.Null(AccountState.Find(state, Id));
        Assert.NotNull(AccountState.Find(state, "1b2c3d"));
    }

    // ---- What a tab starts with --------------------------------------------------------------------------------------

    [Fact]
    public void An_Ollama_tab_reaches_the_server_with_a_placeholder_token_and_every_alias_on_its_model()
    {
        var launch = AccountLaunch.For(Account(LocalServers.Ollama, Ollama()), Deventry);
        var env = Env(launch);

        Assert.Equal("http://localhost:11434", Value(env, "ANTHROPIC_BASE_URL"));
        Assert.Equal("ollama", Value(env, "ANTHROPIC_AUTH_TOKEN"));
        Assert.Equal("", Value(env, "ANTHROPIC_API_KEY"));
        Assert.Null(Settings(launch)["apiKeyHelper"]);
        Assert.Equal("qwen3:8b", Value(env, "ANTHROPIC_DEFAULT_MODEL"));
        Assert.All(AccountLaunch.ModelPins, pin => Assert.Equal("qwen3:8b", Value(env, pin)));
        Assert.Equal("qwen3:8b", launch.Model);
        // No cloud provider takes over, wherever the user turned one on.
        Assert.All(CloudProviders.Switches, s => Assert.Equal("", Value(env, s)));
        Assert.All(CloudProviders.Switches.Append("ANTHROPIC_API_KEY"), v => Assert.True(launch.Environment.ContainsKey(v) && launch.Environment[v] is null, v));
    }

    [Fact]
    public void Claude_Code_is_told_the_models_window_and_the_longest_reply_and_Default_isnt_given_a_million()
    {
        var env = Env(AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => l.LongestReply = 4_096)), Deventry));

        Assert.Equal("40960", Value(env, "CLAUDE_CODE_MAX_CONTEXT_TOKENS"));
        Assert.Equal("4096", Value(env, "CLAUDE_CODE_MAX_OUTPUT_TOKENS"));
        Assert.Equal("1", Value(env, "CLAUDE_CODE_DISABLE_1M_CONTEXT"));
    }

    [Fact]
    public void A_window_that_isnt_known_isnt_told_and_a_longest_reply_out_of_range_is_kept_in_it()
    {
        var env = Env(AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => { l.Window = 0; l.LongestReply = 100_000; })), Deventry));

        Assert.Null(Value(env, "CLAUDE_CODE_MAX_CONTEXT_TOKENS"));
        Assert.Equal("32000", Value(env, "CLAUDE_CODE_MAX_OUTPUT_TOKENS"));
    }

    [Fact]
    public void The_background_model_is_Haikus()
    {
        var env = Env(AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => l.BackgroundModel = " qwen3:1.7b ")), Deventry));

        Assert.Equal("qwen3:1.7b", Value(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL"));
        Assert.Equal("qwen3:8b", Value(env, "ANTHROPIC_DEFAULT_SONNET_MODEL"));
    }

    [Fact]
    public void A_server_with_a_key_gets_it_from_Deventrys_key_helper_and_no_token()
    {
        var local = new LocalServerSettings { Address = "http://localhost:9931", Model = "qwen3", HasKey = true };

        var launch = AccountLaunch.For(Account(LocalServers.LlamaCpp, local), Deventry);
        var env = Env(launch);

        Assert.Equal($"\"{Deventry}\" --api-key-helper {Id}", Settings(launch)["apiKeyHelper"]!.GetValue<string>());
        Assert.Equal("", Value(env, "ANTHROPIC_AUTH_TOKEN"));
        Assert.Null(launch.KeyVariable);
        // The key is in neither the settings nor the environment.
        Assert.DoesNotContain(launch.Environment, e => e.Value is { Length: > 0 });
    }

    [Fact]
    public void Ollamas_cloud_reached_directly_takes_its_key_the_same_way()
    {
        var launch = AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => { l.Address = "https://ollama.com/"; l.HasKey = true; })), Deventry);

        Assert.Equal("https://ollama.com", Value(Env(launch), "ANTHROPIC_BASE_URL"));
        Assert.NotNull(Settings(launch)["apiKeyHelper"]);
    }

    [Fact]
    public void Kept_off_the_internet_Claude_Code_sends_nothing_besides_the_servers_requests()
    {
        var offline = AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => l.KeepOffline = true)), Deventry);
        var online = AccountLaunch.For(Account(LocalServers.Ollama, Ollama()), Deventry);

        Assert.Equal("1", Value(Env(offline), AccountLaunch.NonessentialTrafficVariable));
        Assert.True(Settings(offline)["skipWebFetchPreflight"]!.GetValue<bool>());
        // Any value turns it off, so an account without it leaves the user's own alone.
        Assert.Null(Env(online)[AccountLaunch.NonessentialTrafficVariable]);
        Assert.Null(Settings(online)["skipWebFetchPreflight"]);
    }

    [Fact]
    public void A_tab_starts_without_WebSearch_which_a_local_server_cant_do()
    {
        var launch = AccountLaunch.For(Account(LocalServers.Ollama, Ollama()), Deventry);

        Assert.Equal(["WebSearch"], launch.DisallowedTools);
        var args = ClaudeArguments.ForStreamingSession(new ClaudeLaunchOptions { WorkingDirectory = "/w", DisallowedTools = launch.DisallowedTools });
        var at = args.ToList().IndexOf("--disallowedTools");
        Assert.True(at >= 0);
        Assert.Equal("WebSearch", args[at + 1]);
        Assert.DoesNotContain("--disallowedTools", ClaudeArguments.ForStreamingSession(new ClaudeLaunchOptions { WorkingDirectory = "/w" }));
    }

    [Fact]
    public void An_account_without_an_address_or_a_model_cant_start_a_tab()
    {
        Assert.Throws<InvalidOperationException>(() => AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => l.Address = "not a url ")), Deventry));
        Assert.Throws<InvalidOperationException>(() => AccountLaunch.For(Account(LocalServers.Ollama, Ollama(l => l.Model = " ")), Deventry));
    }

    // ---- The rules -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("localhost:11434", "http://localhost:11434")]
    [InlineData(" http://127.0.0.1:9931/ ", "http://127.0.0.1:9931")]
    [InlineData("https://ollama.com", "https://ollama.com")]
    [InlineData("ftp://host", null)]
    [InlineData("http://host?x=1", null)]
    [InlineData("", null)]
    public void An_address_is_kept_with_its_scheme_and_without_a_slash_at_the_end(string address, string? expected) =>
        Assert.Equal(expected, LocalServers.NormalizeAddress(address));

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:9931", true)]
    [InlineData("http://[::1]:9931", true)]
    [InlineData("http://gpu-box.local:11434", false)]
    [InlineData("https://ollama.com", false)]
    public void Only_an_address_on_this_computer_is_one_Deventry_can_start_a_server_at(string address, bool local)
    {
        Assert.Equal(local, LocalServers.IsOnThisComputer(address));
        Assert.Equal(local ? "on this computer" : $"on {new Uri(address).Host}", LocalServers.Where(address));
    }

    [Theory]
    [InlineData(40_960, 32_000, 7_960)]
    [InlineData(40_960, 8_192, 19_768)]
    [InlineData(65_536, 32_000, 32_536)]
    [InlineData(65_536, 8_192, 44_344)]
    public void Claude_Code_compacts_at_the_window_less_the_longest_reply_and_13000_as_the_spike_measured(int window, int reply, int point) =>
        Assert.Equal(point, LocalServers.CompactionPoint(window, reply));

    [Fact]
    public void Each_server_has_its_default_address_and_token()
    {
        Assert.Equal("http://localhost:11434", LocalServers.Ollama.DefaultAddress);
        Assert.Equal("http://localhost:9931", LocalServers.LlamaCpp.DefaultAddress);
        Assert.Same(LocalServers.Ollama, LocalServers.Find(AccountKinds.Ollama));
        Assert.Same(LocalServers.LlamaCpp, LocalServers.Find(AccountKinds.LlamaCpp));
        Assert.Null(LocalServers.Find(AccountKinds.Bedrock));
    }

    [Fact]
    public void Settings_say_whats_wrong_with_an_account()
    {
        Assert.Null(LocalServers.Problem(LocalServers.Ollama, Ollama()));
        Assert.Contains("address", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.Address = "")));
        Assert.Contains("model", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.Model = "")));
        Assert.Contains("space", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.BackgroundModel = "a b")));
        Assert.Contains("compacts before every turn", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.Window = 8_192)));
        Assert.Contains("longest reply is between", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.LongestReply = 100)));
        // 40,960 with a 20,000-token longest reply leaves less than Claude Code's own instructions take.
        Assert.Contains("too little room", LocalServers.Problem(LocalServers.Ollama, Ollama(l => l.LongestReply = 20_000)));
        // llama-server can only be started with a model to load.
        var llama = new LocalServerSettings { Address = "http://localhost:9931", Model = "qwen3", Window = 65_536 };
        Assert.Contains("llama-server loads", LocalServers.Problem(LocalServers.LlamaCpp, llama));
        llama.StartServer = false;
        Assert.Null(LocalServers.Problem(LocalServers.LlamaCpp, llama));
    }
}
