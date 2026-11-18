using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Agents;
using Deventry.Core.Codex;
using Deventry.Testing;

namespace Deventry.App.Tests;

/// <summary>
/// A Codex tab in a Perforce workspace (DESIGN.md §18, "Perforce"): Deventry's hook, which Codex runs before a patch,
/// trusted once the user says so, and checking the patch's files out first.
/// </summary>
public sealed class CodexPerforceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_codex_tab_asks_to_trust_deventrys_hook_which_then_checks_files_out_before_a_patch()
    {
        var p4 = new FakeP4();
        await using var h = new TabTestHarness(launcher: p4);
        p4.Time = h.Time;
        p4.Root = h.WorkFolder;
        h.Services.Accounts.HelperExecutable = Path.Combine(h.Root, "Deventry.exe");
        var command = CodexHooks.Command(h.Services.Accounts.HelperExecutable);
        h.Transport.RpcAnswers["hooks/list"] = _ => new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["cwd"] = h.WorkFolder,
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["key"] = @"C:\<session-flags>\config.toml:pre_tool_use:0:0",
                    ["command"] = command,
                    ["currentHash"] = "sha256:abc",
                    ["trustStatus"] = "untrusted",
                }),
            }),
        };
        var player = Path.Combine(h.WorkFolder, "Player.cpp");
        await File.WriteAllTextAsync(player, "a", Token);
        p4.Has(player, "a");

        await InlineDispatcher.RunAsync(() => h.Shell.OpenFolderAsync(h.WorkFolder, agent: CodexAgent.Instance));
        var tab = h.Shell.SelectedTab!;

        var launch = await Launched(h);
        Assert.Contains(CodexHooks.Config(command), launch.ConfigOverrides);
        var pipe = launch.EnvironmentOverrides[CodexHooks.PipeVariable];
        Assert.NotNull(pipe);
        await TabTestHarness.Eventually(() => h.Shell.Confirmation?.Title == "Let Codex check files out of Perforce?", "the question");

        await InlineDispatcher.RunAsync(() => h.Shell.Confirmation!.ConfirmCommand.ExecuteAsync(null));

        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Contains("config/batchWrite"), "the trust");
        var edit = h.Transport.Sent.Last(m => m["method"]?.GetValue<string>() == "config/batchWrite")["params"]!["edits"]![0]!;
        Assert.Equal(@"hooks.state.""C:\\<session-flags>\\config.toml:pre_tool_use:0:0"".trusted_hash", edit["keyPath"]!.GetValue<string>());
        Assert.Equal("sha256:abc", edit["value"]!.GetValue<string>());

        // Codex runs the helper before its patch, which asks the tab.
        var answer = await CodexHooks.RelayAsync(new JsonObject
        {
            ["hook_event_name"] = "PreToolUse",
            ["tool_name"] = CodexHooks.PatchTool,
            ["cwd"] = h.WorkFolder,
            ["tool_input"] = new JsonObject { ["command"] = "*** Begin Patch\n*** Update File: Player.cpp\n@@\n-a\n+b\n*** Add File: Hero.cpp\n+new\n*** End Patch\n" },
        }.ToJsonString(), pipe, TimeProvider.System, Token);

        Assert.Equal("{}", answer);
        Assert.Equal("edit", Assert.Single(p4.FileCommands).Command);
        Assert.Equal("edit", p4.Find(player)!.Action);
        Assert.True(tab.IsCodex);
    }

    [Fact]
    public async Task Outside_a_perforce_workspace_a_codex_tab_has_no_hook()
    {
        await using var h = new TabTestHarness();
        h.Services.Accounts.HelperExecutable = Path.Combine(h.Root, "Deventry.exe");

        await InlineDispatcher.RunAsync(() => h.Shell.OpenFolderAsync(h.WorkFolder, agent: CodexAgent.Instance));

        var launch = await Launched(h);
        Assert.DoesNotContain(launch.ConfigOverrides, c => c.StartsWith("hooks.", StringComparison.Ordinal));
        Assert.False(launch.EnvironmentOverrides.ContainsKey(CodexHooks.PipeVariable));
        Assert.DoesNotContain("hooks/list", h.Transport.SentRpcMethods);
    }

    private static async Task<CodexLaunchOptions> Launched(TabTestHarness h)
    {
        await TabTestHarness.Eventually(() => h.CodexFactory.Launches.Count == 1 && h.Shell.SelectedTab!.Status == TabStatus.Idle, "the Codex tab to start");
        return h.CodexFactory.Launches.Single();
    }
}
