using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.Core.Installation;
using Claudette.Core.Processes;
using Claudette.Core.Sessions;
using Claudette.Core.Settings;
using Claudette.Core.Subagents;
using Claudette.MockApi;

namespace Claudette.App.Tests;

/// <summary>
/// Subagents in a tab running the real <c>claude</c> against the mock Messages API (DESIGN.md §5, "Subagents"): their
/// rows under the tab and their work in it, and Settings → Claude Code → Subagents → Don't allow turning them down. No
/// account and no tokens. Skipped when Claude Code isn't installed.
/// </summary>
[Trait("Category", "RealCli")]
public sealed class SubagentRealCliTests
{
    [Fact]
    public async Task A_fan_out_gets_rows_under_the_tab_and_a_rows_work_shows_in_it()
    {
        await using var api = await MockAnthropicApi.StartAsync();
        await using var h = await StartAsync(api, s => s.Appearance.ShowSubagents = SubagentDisplay.InSidebar);
        var tab = await h.OpenTabAsync();

        // Two subagents, one asking to run a command and the other starting a nested Explore (the mock's SUBAGENTS).
        tab.ComposerText = "SUBAGENTS";
        await tab.SendCommand.ExecuteAsync(null);
        await Claudette.Tests.Waiting.UntilAsync(() => InlineDispatcher.Read(() => tab.Items.OfType<PermissionItem>().Any(p => p.IsPending)), "the subagent's prompt", TimeSpan.FromSeconds(60));
        var asking = InlineDispatcher.Read(() => tab.SubagentRows.Single(r => r.Node.IsWaiting));
        InlineDispatcher.Run(() => asking.OpenCommand.Execute(null));
        var prompt = InlineDispatcher.Read(() => Assert.IsType<PermissionItem>(tab.ViewedAgent!.WaitingPrompt));
        InlineDispatcher.Run(() => prompt.AllowCommand.Execute(null));
        await Claudette.Tests.Waiting.UntilAsync(() => InlineDispatcher.Read(() => tab.Items.OfType<TurnSummaryItem>().Any()), "the end of the turn", TimeSpan.FromSeconds(60));

        InlineDispatcher.Run(() =>
        {
            Assert.Equal(["Touch a marker file", "Delegate a deeper look", "Search deeper"], tab.SubagentRows.Select(r => r.Node.Title));
            Assert.Equal([0d, 0d, 12d], tab.SubagentRows.Select(r => r.Indent));
            Assert.All(tab.SubagentRows, r => Assert.True(r.Node.IsDone));
            Assert.Same(asking.Node, tab.ViewedAgent);
            Assert.Contains(tab.ShownItems.ToolCalls(), t => t.Name == "Bash");
        });
    }

    [Fact]
    public async Task Dont_allow_turns_every_subagent_down_and_Claude_carries_on()
    {
        await using var api = await MockAnthropicApi.StartAsync();
        await using var h = await StartAsync(api, s =>
        {
            s.ClaudeCode.Subagents = SubagentUse.DontAllow;
            s.Appearance.ShowSubagents = SubagentDisplay.InSidebar;
        });
        var tab = await h.OpenTabAsync();

        tab.ComposerText = "SUBAGENTS";
        await tab.SendCommand.ExecuteAsync(null);
        await Claudette.Tests.Waiting.UntilAsync(() => InlineDispatcher.Read(() => tab.Items.OfType<TurnSummaryItem>().Any()), "the end of the turn", TimeSpan.FromSeconds(60));

        InlineDispatcher.Run(() =>
        {
            var cards = tab.Items.OfType<SubagentItem>().ToArray();
            Assert.Equal(2, cards.Length);
            Assert.All(cards, c =>
            {
                Assert.True(c.IsRefused);
                Assert.False(c.IsError);
                Assert.Empty(c.Items);
            });
            Assert.False(tab.HasAgents);
            Assert.Empty(tab.SubagentRows);
            Assert.DoesNotContain(tab.Items, i => i is PermissionItem);
        });
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, "agent-marker.txt")));
        Assert.Contains(api.Requests, r => r.System.Contains(SubagentMessages.StartNote(SubagentUse.DontAllow)!, StringComparison.Ordinal));
        Assert.Contains(api.Requests, r => r.LastToolResultText.Contains(SubagentMessages.Refused, StringComparison.Ordinal));
    }

    /// <summary>A harness whose tabs run the real <c>claude</c> pointed at the mock, in a configuration folder of its own.</summary>
    private static async Task<TabTestHarness> StartAsync(MockAnthropicApi api, Action<AppSettings> configure)
    {
        var located = await new ClaudeLocator(new ProcessLauncher(), TimeProvider.System).LocateAsync(null, TestContext.Current.CancellationToken);
        RealCli.SkipUnlessInstalled(located.IsUsable);
        var h = new TabTestHarness(configure);
        var config = Directory.CreateDirectory(Path.Combine(h.Root, "claude-config")).FullName;
        h.Services.UseSessionFactory(new MockedFactory(new ClaudeSessionFactory(located.Install!.Path, new ProcessLauncher(), TimeProvider.System), new Dictionary<string, string?>
        {
            ["ANTHROPIC_BASE_URL"] = api.BaseAddress.ToString().TrimEnd('/'),
            ["ANTHROPIC_API_KEY"] = "sk-ant-mock-000",
            ["ANTHROPIC_AUTH_TOKEN"] = null,
            ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
            ["CLAUDE_CONFIG_DIR"] = config,
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
            // The nested subagent needs a level of nesting, which a user's environment can take away.
            ["CLAUDE_CODE_MAX_SUBAGENT_SPAWN_DEPTH"] = "3",
        }));
        return h;
    }

    /// <summary>Starts the real <c>claude</c> pointed at the mock, keeping the tab's own variables.</summary>
    private sealed class MockedFactory(IClaudeSessionFactory inner, IReadOnlyDictionary<string, string?> environment) : IClaudeSessionFactory
    {
        public Task<ClaudeSession> StartAsync(ClaudeLaunchOptions options, CancellationToken cancellationToken = default)
        {
            var merged = new Dictionary<string, string?>(options.EnvironmentOverrides);
            foreach (var (name, value) in environment)
            {
                merged[name] = value;
            }
            return inner.StartAsync(options with { EnvironmentOverrides = merged, Model = "claude-haiku-4-5" }, cancellationToken);
        }
    }
}
