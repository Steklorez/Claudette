using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Agents;
using Deventry.Core.Codex;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>A tab that runs Codex (DESIGN.md §18, "Codex, a second agent"), on the harness's scripted Codex.</summary>
public sealed class CodexTabTests
{
    private static async Task<TabViewModel> OpenCodexTabAsync(TabTestHarness h)
    {
        await InlineDispatcher.RunAsync(() => h.Shell.OpenFolderAsync(h.WorkFolder, agent: CodexAgent.Instance));
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the Codex tab to start");
        return tab;
    }

    [Fact]
    public async Task A_codex_tab_starts_codex_and_its_turn_shows_like_any_other()
    {
        await using var h = new TabTestHarness();
        var tab = await OpenCodexTabAsync(h);

        Assert.True(tab.IsCodex);
        Assert.Equal("Codex", tab.AgentShortName);
        var launch = Assert.Single(h.CodexFactory.Launches);
        Assert.Equal(h.WorkFolder, launch.Start.WorkingDirectory);
        Assert.Contains(CodexArguments.ClaudeMdFallback, launch.ConfigOverrides);
        Assert.Empty(h.Factory.Launches);
        Assert.Equal(ScriptedTransport.CodexThread, tab.State.SessionId);
        Assert.Equal("GPT Fake 1", tab.ModelName);

        tab.ComposerText = "hello";
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Contains("turn/start", h.Transport.SentRpcMethods);
        h.Transport.EmitCodexTurn("pong");

        await TabTestHarness.Eventually(() => tab.Items.OfType<TurnSummaryItem>().Any(), "the turn to end");
        Assert.Equal("pong", tab.Items.OfType<AssistantTextItem>().Last().Text);
        Assert.Equal(TabStatus.Idle, tab.Status);
    }

    private static TabViewModel RestoredCodexTab(TabTestHarness h, string thread)
    {
        Directory.CreateDirectory(h.WorkFolder);
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, SessionId = thread, Agent = AgentKinds.Codex }];
        h.Shell.Restore(null);
        return h.Shell.AllTabs.Single();
    }

    [Fact]
    public async Task A_restored_codex_tab_starts_as_its_shown_and_reads_back_its_conversation()
    {
        await using var h = new TabTestHarness();
        h.Transport.RpcAnswers["thread/turns/list"] = p => new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["id"] = "turn-1",
                ["items"] = new JsonArray(
                    new JsonObject { ["type"] = "userMessage", ["id"] = "u1", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Fix the jump" }) },
                    new JsonObject { ["type"] = "agentMessage", ["id"] = "m1", ["text"] = "Fixed." }),
            }),
            ["nextCursor"] = null,
        };
        var tab = RestoredCodexTab(h, "codex-thread-9");

        tab.ReadEarlierConversation();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.Items.OfType<NoteItem>().Any(n => n.Text == "Resumed."), "the earlier conversation");
        Assert.Equal("codex-thread-9", Assert.Single(h.CodexFactory.Launches).Start.Resume);
        Assert.Equal("Fix the jump", tab.Items.OfType<UserMessageItem>().Single().Text);
        Assert.Equal("Fixed.", tab.Items.OfType<AssistantTextItem>().Single().Text);
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task A_restored_codex_tab_whose_thread_is_gone_offers_a_new_session()
    {
        await using var h = new TabTestHarness();
        h.Transport.RpcAnswers["thread/read"] = _ => ScriptedTransport.RpcError("no rollout found for thread id codex-thread-9");
        var tab = RestoredCodexTab(h, "codex-thread-9");

        await tab.EnsureStartedAsync();

        Assert.True(tab.IsSessionMissing);
        Assert.Equal(TabViewModel.CodexSessionMissingText, tab.SessionMissingMessage);
        Assert.Equal("Its earlier conversation is gone", tab.RowDetail);
        h.Transport.RpcAnswers.TryRemove("thread/read", out _);

        await tab.StartNewSessionCommand.ExecuteAsync(null);

        Assert.False(tab.IsSessionMissing);
        Assert.Null(h.CodexFactory.Launches.Last().Start.Resume);
        Assert.Equal(ScriptedTransport.CodexThread, tab.State.SessionId);
        Assert.Equal(TabStatus.Idle, tab.Status);
    }

    [Fact]
    public async Task A_folder_remembers_the_agent_its_last_new_tab_used()
    {
        await using var h = new TabTestHarness();
        await OpenCodexTabAsync(h);

        await InlineDispatcher.RunAsync(() => h.Shell.OpenFolderAsync(h.WorkFolder));

        var second = h.Shell.SelectedTab!;
        Assert.True(second.IsCodex);
        Assert.Equal(AgentKinds.Codex, AgentsService.ForFolder(h.Services.State, h.WorkFolder).Kind);
    }

    [Fact]
    public async Task A_codex_tabs_modes_are_codexs_and_full_access_asks_first()
    {
        await using var h = new TabTestHarness();
        var tab = await OpenCodexTabAsync(h);

        Assert.Equal([CodexModes.ReadOnly, CodexModes.Auto, CodexModes.Plan, CodexModes.FullAccess], tab.PermissionModeChoices.Select(c => c.Value));
        Assert.Equal("Auto", tab.PermissionModeName);
        Assert.True(tab.PermissionModeChoices.Single(c => c.Value == CodexModes.FullAccess).IsBypass);

        await tab.ChooseModeCommand.ExecuteAsync(tab.PermissionModeChoices.Single(c => c.Value == CodexModes.ReadOnly));

        await TabTestHarness.Eventually(() => tab.PermissionModeName == "Read only", "the mode to change");
        var update = h.Transport.Sent.Last(m => m["method"]?.GetValue<string>() == "thread/settings/update")["params"]!.AsObject();
        Assert.Equal("readOnly", update["sandboxPolicy"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_command_codex_runs_is_a_card_with_its_output()
    {
        await using var h = new TabTestHarness();
        var tab = await OpenCodexTabAsync(h);
        tab.ComposerText = "status";
        await tab.SendCommand.ExecuteAsync(null);

        h.Transport.Emit(new JsonObject { ["method"] = "turn/started", ["params"] = new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = ScriptedTransport.CodexTurn } } });
        var command = new JsonObject
        {
            ["type"] = "commandExecution",
            ["id"] = "exec-1",
            ["command"] = "/bin/bash -lc 'git status'",
            ["status"] = "inProgress",
            ["commandActions"] = new JsonArray(new JsonObject { ["type"] = "unknown", ["command"] = "git status" }),
        };
        h.Transport.Emit(new JsonObject { ["method"] = "item/started", ["params"] = new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["item"] = command } });
        h.Transport.Emit(new JsonObject
        {
            ["method"] = "item/completed",
            ["params"] = new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["item"] = new JsonObject { ["type"] = "commandExecution", ["id"] = "exec-1", ["status"] = "completed", ["aggregatedOutput"] = "nothing to commit", ["exitCode"] = 0 } },
        });

        await TabTestHarness.Eventually(() => tab.Items.ToolCalls().Any(t => t.IsComplete), "the command to finish");
        var card = tab.Items.ToolCalls().Single();
        Assert.Equal(ToolKind.Command, card.Call.Kind);
        Assert.Equal("git status", card.Command);
        Assert.Equal("nothing to commit", card.Output);
        Assert.Equal("IconToolBash", card.IconKey);
    }

    [Fact]
    public async Task The_header_shows_the_selected_tabs_agents_plan_with_its_icon_while_both_are_open()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        using var header = new UsageViewModel(h.Services, tracker);
        var claude = await h.OpenTabAsync();
        Assert.False(h.Shell.HasSeveralAgents);
        Assert.False(claude.ShowsAgentIcon);
        var codex = await OpenCodexTabAsync(h);

        h.Transport.Emit(new JsonObject
        {
            ["method"] = "account/rateLimits/updated",
            ["params"] = new JsonObject
            {
                ["rateLimits"] = new JsonObject
                {
                    ["primary"] = new JsonObject { ["usedPercent"] = 42, ["windowDurationMins"] = 300, ["resetsAt"] = h.Time.GetUtcNow().AddHours(2).ToUnixTimeSeconds() },
                    ["secondary"] = new JsonObject { ["usedPercent"] = 12, ["windowDurationMins"] = 10080 },
                },
            },
        });
        await TabTestHarness.Eventually(() => tracker.CodexCurrent is not null, "Codex's limits");
        InlineDispatcher.Run(() => header.ShowAgent(h.Shell.SelectedAgent));

        Assert.True(h.Shell.HasSeveralAgents);
        Assert.True(codex.ShowsAgentIcon && claude.ShowsAgentIcon);
        Assert.Equal("Codex", header.AgentName);
        Assert.Equal("IconAgentCodex", header.AgentIconKey);
        Assert.Equal(42, header.Session.Percent);
        Assert.Equal("12%", Assert.Single(header.Weekly).PercentText);

        InlineDispatcher.Run(() => header.ShowAgent(claude.Agent));
        Assert.Equal("Claude Code", header.AgentName);
        Assert.False(header.HasData);
    }
}
