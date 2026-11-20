using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// A tab that hasn't started yet still has its model picker (DESIGN.md §5, "Model &amp; effort"): the models Claude Code
/// last offered on this machine, Claude Code's own default named rather than left empty, and a choice that asks about the
/// cache only while there's one to lose.
/// </summary>
public class UnstartedTabPickerTests
{
    // The harness's clock starts on Monday 28 September 2026 at 12:00 UTC.

    private static readonly ModelInfo[] Remembered =
    [
        new ModelInfo("opus", "claude-opus-5-5", "Opus", "Opus 5.5 · Best for everyday, complex tasks", true, ["low", "high"]),
        new ModelInfo("haiku", "claude-haiku-5-5", "Haiku", "Haiku 5.5 · Fastest for quick answers", true, ["low"]),
    ];

    /// <summary>A restored tab whose conversation's last reply came at <paramref name="repliedAt"/>, written to the 1-hour cache.</summary>
    private static async Task<TabViewModel> RestoreAsync(TabTestHarness h, string repliedAt, string? model = null)
    {
        h.Services.State.KnownModels = [.. Remembered];
        var reply = Wire.Message(new JsonObject { ["type"] = "text", ["text"] = "A missing `using`." });
        reply["usage"] = new JsonObject
        {
            ["input_tokens"] = 12, ["output_tokens"] = 40, ["cache_creation_input_tokens"] = 30_000,
            ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = 30_000 },
        };
        h.WriteTranscript("s1",
            Wire.Entry("user", "2026-09-28T09:00:00Z", new JsonObject { ["role"] = "user", ["content"] = "Why is the build red?" }),
            Wire.Entry("assistant", repliedAt, reply));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, SessionId = "s1", Overrides = new TabOverrides { Model = model } }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();
        await TabTestHarness.Eventually(() => tab.IsPromptCacheShown, "the restored conversation");
        Assert.Equal(TabStatus.NotStarted, tab.Status);
        return tab;
    }

    [Fact]
    public async Task A_tab_that_hasnt_started_offers_the_models_last_offered_and_starts_in_the_one_chosen()
    {
        await using var h = new TabTestHarness();
        var tab = await RestoreAsync(h, "2026-09-28T10:00:00Z");

        Assert.Equal(["opus", "haiku"], tab.Models.Select(m => m.Value));
        Assert.Equal("Default", tab.ModelName);

        // Its cache expired an hour ago: there's nothing to lose, so no question.
        InlineDispatcher.Run(() => tab.ChooseModelCommand.Execute(tab.Models[1]));
        Assert.False(tab.ModelSwitch.IsOpen);

        await TabTestHarness.Eventually(() => tab.State.Overrides.Model == "haiku", "the switch");
        Assert.Contains("set_model", h.Transport.SentControlSubtypes);
        Assert.Equal("Haiku", tab.ModelName);
    }

    [Fact]
    public async Task Choosing_a_model_before_the_tab_starts_asks_while_its_conversation_is_in_the_cache()
    {
        await using var h = new TabTestHarness();
        var tab = await RestoreAsync(h, "2026-09-28T11:40:00Z");

        InlineDispatcher.Run(() => tab.ChooseModelCommand.Execute(tab.Models[1]));

        Assert.True(tab.ModelSwitch.IsOpen);
        Assert.Equal(TabStatus.NotStarted, tab.Status);
        await InlineDispatcher.RunAsync(() => tab.ModelSwitch.ConfirmCommand.ExecuteAsync(null));
        Assert.Contains("set_model", h.Transport.SentControlSubtypes);
        Assert.Equal("haiku", tab.State.Overrides.Model);
    }

    [Fact]
    public async Task Choosing_the_model_a_tab_will_start_in_leaves_it_as_it_is()
    {
        await using var h = new TabTestHarness();
        var tab = await RestoreAsync(h, "2026-09-28T11:40:00Z", model: "opus");
        Assert.Equal("Opus", tab.ModelName);

        InlineDispatcher.Run(() => tab.ChooseModelCommand.Execute(tab.Models[0]));

        Assert.False(tab.ModelSwitch.IsOpen);
        Assert.Equal(TabStatus.NotStarted, tab.Status);
        Assert.True(tab.IsPromptCacheShown);
    }
}
