using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Agents;
using Deventry.Core.Codex;
using Deventry.Core.Sessions;

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
}
