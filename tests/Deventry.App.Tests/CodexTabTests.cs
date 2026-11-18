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

    [Fact]
    public async Task Codexs_limits_alert_by_name_and_its_line_shows_with_its_plan()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        using var header = new UsageViewModel(h.Services, tracker);
        var resets = h.Time.GetUtcNow().AddHours(3).ToUnixTimeSeconds();
        JsonObject Limits(int percent) => new() { ["primary"] = new JsonObject { ["usedPercent"] = percent, ["windowDurationMins"] = 300, ["resetsAt"] = resets } };

        InlineDispatcher.Run(() => tracker.OnCodexRateLimits(Limits(50)));
        h.Time.Advance(TimeSpan.FromMinutes(5));
        InlineDispatcher.Run(() => tracker.OnCodexRateLimits(Limits(85)));

        await TabTestHarness.Eventually(() => h.Notifier.Shown.Any(n => n.Title == "Codex: Session usage passed 75%"), "Codex's notification");
        // Claude Code's plan is shown, so its line waits for Codex's.
        Assert.False(header.HasAlert);
        InlineDispatcher.Run(() => header.ShowAgent(CodexAgent.Instance));
        // The latest: at this pace the session runs out before it resets.
        Assert.Equal("On pace to hit the session limit", header.Alert!.Title);
        Assert.Contains(h.Notifier.Shown, n => n.Title == "Codex: On pace to hit the session limit");
        InlineDispatcher.Run(() => header.ShowAgent(ClaudeCodeAgent.Instance));
        Assert.False(header.HasAlert);
    }

    [Fact]
    public async Task The_usage_panel_names_the_shown_agent_and_gives_the_others_plan_a_line()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        using var header = new UsageViewModel(h.Services, tracker);
        using var panel = new UsagePanelViewModel(h.Services, tracker, header, _ => null);
        Assert.Equal(("This session (5-hour window)", false), (panel.SessionTitle, panel.HasOtherAgent));

        InlineDispatcher.Run(() =>
        {
            tracker.OnCodexRateLimits(new JsonObject
            {
                ["primary"] = new JsonObject { ["usedPercent"] = 42, ["windowDurationMins"] = 300, ["resetsAt"] = h.Time.GetUtcNow().AddHours(2).ToUnixTimeSeconds() },
                ["secondary"] = new JsonObject { ["usedPercent"] = 12, ["windowDurationMins"] = 10080 },
            });
            // Tabs of both agents are open, as the shell says.
            header.ShowsAgentIcon = true;
        });

        Assert.Equal("Claude Code: this session (5-hour window)", panel.SessionTitle);
        Assert.Equal(("Codex", "IconAgentCodex", "Session 42%, resets in 2h 00m · Weekly 12%"), (panel.OtherAgentName, panel.OtherAgentIconKey, panel.OtherAgentText));
        InlineDispatcher.Run(() => header.ShowAgent(CodexAgent.Instance));
        Assert.Equal("Codex: this session (5-hour window)", panel.SessionTitle);
        // Claude Code has no reading yet.
        Assert.False(panel.HasOtherAgent);
    }

    [Fact]
    public async Task A_codex_tab_runs_under_codexs_own_sign_in_not_its_groups_account()
    {
        await using var h = new TabTestHarness();
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        h.Services.Accounts.HelperExecutable = Path.Combine(h.Root, "Deventry.exe");
        var work = await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work");
        h.Services.Accounts.SetForFolder(h.WorkFolder, work.Id);

        var tab = await OpenCodexTabAsync(h);

        Assert.Null(tab.Account);
        Assert.False(tab.ShowsAccountTag);
        Assert.False(tab.HasAccountMenu);
        Assert.Equal("Codex's own sign-in", tab.AccountName);
        Assert.Empty(h.CodexFactory.Launches.Single().EnvironmentOverrides);
    }

    [Fact]
    public async Task Codexs_plan_is_a_plan_card_and_its_steps_are_the_to_do_list()
    {
        await using var h = new TabTestHarness(s => s.Codex.DefaultMode = CodexModes.Plan);
        var tab = await OpenCodexTabAsync(h);
        tab.ComposerText = "Plan the grappling hook";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Contains("turn/start"), "the message");
        JsonObject Note(string method, JsonObject parameters) => new() { ["method"] = method, ["params"] = parameters };
        h.Transport.Emit(Note("turn/started", new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = ScriptedTransport.CodexTurn } }));
        h.Transport.Emit(Note("turn/plan/updated", new JsonObject
        {
            ["threadId"] = ScriptedTransport.CodexThread,
            ["turnId"] = ScriptedTransport.CodexTurn,
            ["plan"] = new JsonArray(new JsonObject { ["step"] = "Read the movement code", ["status"] = "completed" }, new JsonObject { ["step"] = "Write the plan", ["status"] = "inProgress" }),
        }));
        h.Transport.Emit(Note("item/completed", new JsonObject
        {
            ["threadId"] = ScriptedTransport.CodexThread,
            ["turnId"] = ScriptedTransport.CodexTurn,
            ["item"] = new JsonObject { ["type"] = "plan", ["id"] = "plan-1", ["text"] = "1. Add the hook\n2. Test it" },
        }));
        h.Transport.Emit(Note("turn/completed", new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = ScriptedTransport.CodexTurn, ["status"] = "completed" } }));

        // Waiting for the user, as for Claude Code's plan.
        await TabTestHarness.Eventually(() => tab.Items.OfType<PlanItem>().Any() && tab.Status == TabStatus.NeedsInput, "the plan card");
        var plan = tab.Items.OfType<PlanItem>().Single();
        Assert.Equal("Codex's plan", plan.Title);
        Assert.False(plan.OffersAcceptingEdits);
        Assert.Equal(["Read the movement code", "Write the plan"], tab.TodoList.Items.Select(t => t.Content));
        Assert.Equal(["completed", "in_progress"], tab.TodoList.Items.Select(t => t.Status));

        InlineDispatcher.Run(() => plan.ApproveInAutoModeCommand.Execute(null));

        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Count(m => m == "turn/start") == 2, "the plan to be carried out");
        var start = h.Transport.Sent.Last(m => m["method"]?.GetValue<string>() == "turn/start");
        Assert.Equal(CodexSession.ImplementPlan, start["params"]!["input"]![0]!["text"]!.GetValue<string>());
        h.Transport.Emit(Note("turn/started", new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = "codex-turn-2" } }));
        await TabTestHarness.Eventually(() => tab.PermissionModeName == "Auto", "the mode to leave Plan");
    }

    [Fact]
    public async Task A_codex_subagent_is_a_subagent_card_and_a_node_on_the_agent_map_it_can_be_stopped_from()
    {
        const string Child = "codex-child-1";
        await using var h = new TabTestHarness();
        var tab = await OpenCodexTabAsync(h);
        tab.ComposerText = "Look at the levels with a helper";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Contains("turn/start"), "the message");
        JsonObject Note(string method, JsonObject parameters) => new() { ["method"] = method, ["params"] = parameters };
        JsonObject Item(string method, string thread, JsonObject item) => Note(method, new JsonObject { ["threadId"] = thread, ["turnId"] = "t", ["item"] = item });
        h.Transport.Emit(Note("turn/started", new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = ScriptedTransport.CodexTurn } }));
        h.Transport.Emit(Item("item/started", ScriptedTransport.CodexThread, new JsonObject { ["type"] = "collabAgentToolCall", ["id"] = "spawn-1", ["tool"] = "spawnAgent", ["prompt"] = "Count the levels", ["receiverThreadIds"] = new JsonArray(Child), ["status"] = "inProgress" }));
        h.Transport.Emit(Note("turn/started", new JsonObject { ["threadId"] = Child, ["turn"] = new JsonObject { ["id"] = "child-turn" } }));
        h.Transport.Emit(Item("item/started", Child, new JsonObject { ["type"] = "commandExecution", ["id"] = "exec-c1", ["command"] = "ls levels", ["status"] = "inProgress" }));

        await TabTestHarness.Eventually(() => tab.Items.OfType<SubagentItem>().SingleOrDefault() is { Items.Count: > 0 }, "the subagent's work");
        var subagent = tab.Items.OfType<SubagentItem>().Single();
        Assert.Equal("Count the levels", subagent.Call.Prompt);
        var node = Assert.Single(tab.Agents.Subagents);
        await TabTestHarness.Eventually(() => node.CanStop, "Stop on the map");
        Assert.Equal(Child, node.TaskId);

        h.Transport.Emit(Item("item/completed", Child, new JsonObject { ["type"] = "agentMessage", ["id"] = "m-c1", ["text"] = "Three levels." }));
        h.Transport.Emit(Note("turn/completed", new JsonObject { ["threadId"] = Child, ["turn"] = new JsonObject { ["id"] = "child-turn", ["status"] = "completed" } }));

        await TabTestHarness.Eventually(() => subagent.IsSucceeded && node.IsDone, "the subagent to report");
        Assert.True(tab.IsInTurn);
    }

    [Fact]
    public async Task With_subagents_turned_off_codex_starts_without_them_and_one_it_starts_anyway_is_stopped()
    {
        const string Child = "codex-child-1";
        await using var h = new TabTestHarness(s => s.ClaudeCode.Subagents = Core.Settings.SubagentUse.DontAllow);
        var tab = await OpenCodexTabAsync(h);

        var launch = Assert.Single(h.CodexFactory.Launches);
        Assert.Contains(CodexArguments.NoSubagents, launch.ConfigOverrides);
        Assert.Contains(Core.Subagents.SubagentMessages.StartNote(Core.Settings.SubagentUse.DontAllow, Core.Subagents.SubagentMessages.CodexTool)!, launch.Start.DeveloperInstructions);

        tab.ComposerText = "Count the levels";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Contains("turn/start"), "the message");
        JsonObject Note(string method, JsonObject parameters) => new() { ["method"] = method, ["params"] = parameters };
        h.Transport.Emit(Note("turn/started", new JsonObject { ["threadId"] = ScriptedTransport.CodexThread, ["turn"] = new JsonObject { ["id"] = ScriptedTransport.CodexTurn } }));
        h.Transport.Emit(Note("item/started", new JsonObject
        {
            ["threadId"] = ScriptedTransport.CodexThread,
            ["turnId"] = ScriptedTransport.CodexTurn,
            ["item"] = new JsonObject { ["type"] = "collabAgentToolCall", ["id"] = "spawn-1", ["tool"] = "spawnAgent", ["prompt"] = "Count them", ["receiverThreadIds"] = new JsonArray(Child), ["status"] = "inProgress" },
        }));

        await TabTestHarness.Eventually(() => h.Transport.Sent.Any(m => m["method"]?.GetValue<string>() == "turn/interrupt" && m["params"]?["threadId"]?.GetValue<string>() == Child), "the subagent to be stopped");
        Assert.Contains(tab.Items.OfType<NoteItem>(), n => n.Text.StartsWith("Stopped a subagent Codex started", StringComparison.Ordinal));
    }
}
