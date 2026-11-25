using System.Diagnostics;
using Deventry.Core.Accounts;
using Deventry.Core.Installation;
using Deventry.Core.Processes;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.IntegrationTests.Support;
using Deventry.MockApi;

namespace Deventry.IntegrationTests;

/// <summary>
/// The real <c>claude</c> as an Ollama or llama.cpp account's tab starts it (DESIGN.md §18, "Ollama and llama.cpp
/// accounts"), with the mock Messages API standing in for the server's, on a configuration that was never signed in.
/// Skipped when Claude Code isn't installed.
/// </summary>
[Trait("Category", "RealCli")]
public sealed class LocalAccountRealCliTests : IAsyncLifetime
{
    private readonly TempFolder _root = new("deventry-local-realcli");
    private MockAnthropicApi _api = null!;
    private ClaudeSessionFactory? _factory;

    private string Work => _root.Combine("repo");

    private string Config => _root.Combine("config");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Config);
        Process.Start(new ProcessStartInfo("git", ["init", "-q"]) { WorkingDirectory = Work, CreateNoWindow = true })?.WaitForExit(10_000);
        _api = await MockAnthropicApi.StartAsync();
        var located = await new ClaudeLocator(new ProcessLauncher(), TimeProvider.System).LocateAsync(null);
        if (located.IsUsable)
        {
            _factory = new ClaudeSessionFactory(located.Install!.Path, new ProcessLauncher(), TimeProvider.System);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        _root.Dispose();
    }

    private ClaudeAccount Ollama(int window = 40_960) => new()
    {
        Name = "Home",
        Kind = AccountKinds.Ollama,
        Local = new LocalServerSettings { Address = _api.BaseAddress.ToString(), Model = "qwen3:8b", Window = window },
    };

    [Fact]
    public async Task A_tab_runs_on_the_accounts_model_with_its_window_and_no_sign_in()
    {
        var launch = AccountLaunch.For(Ollama(), "/opt/Deventry/Deventry");
        await using var session = await StartAsync(launch);

        var init = session.Initialization ?? await session.InitializeAsync(TestContext.Current.CancellationToken);
        // Every entry leads to the account's model, Default without a "[1m]" that would make its window a million.
        Assert.All(init.Models, m => Assert.Equal("qwen3:8b", m.ResolvedModel));
        var context = await session.GetContextUsageAsync(TestContext.Current.CancellationToken);
        Assert.Equal(40_960, context.MaxTokens);

        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        var (done, seen) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("pong", done.Result.Result);
        var asked = Assert.Single(_api.Requests, r => r.Path.StartsWith("/v1/messages", StringComparison.Ordinal) && r.LastUserText.Contains("hello", StringComparison.Ordinal) && r.ToolCount > 0);
        Assert.Equal("qwen3:8b", asked.Model);
        Assert.Contains("ollama", asked.Authorization ?? "", StringComparison.Ordinal);
        // The server can't search, so the tab has no WebSearch.
        var started = Assert.Single(seen.OfType<TurnStarted>());
        Assert.DoesNotContain("WebSearch", started.Init.Raw.GetStringList("tools"));
        Assert.Contains("Bash", started.Init.Raw.GetStringList("tools"));
    }

    [Fact]
    public async Task A_window_smaller_than_Claude_Codes_own_prompt_stops_the_turn_before_it_reaches_the_server()
    {
        var launch = AccountLaunch.For(Ollama(window: 8_192), "/opt/Deventry/Deventry");
        await using var session = await StartAsync(launch);

        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        var (done, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.True(done.Result.IsError);
        Assert.DoesNotContain(_api.Requests, r => r.LastUserText.Contains("hello", StringComparison.Ordinal) && r.ToolCount > 0);
    }

    private async Task<ClaudeSession> StartAsync(AccountLaunch launch)
    {
        RealCli.SkipUnlessInstalled(_factory is not null);
        // Nothing of the user's: no key or sign-in, and a configuration folder of its own, never signed in.
        var overrides = new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = null,
            ["ANTHROPIC_AUTH_TOKEN"] = null,
            ["ANTHROPIC_BASE_URL"] = null,
            ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
            ["CLAUDE_CONFIG_DIR"] = Config,
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
        };
        foreach (var (name, value) in launch.Environment)
        {
            overrides[name] = value;
        }
        return await _factory!.StartAsync(new ClaudeLaunchOptions
        {
            WorkingDirectory = Work,
            Model = launch.Model,
            Settings = launch.Settings,
            DisallowedTools = launch.DisallowedTools,
            EnvironmentOverrides = overrides,
        }, TestContext.Current.CancellationToken);
    }
}
