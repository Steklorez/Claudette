using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// A tab that hasn't started yet still has its model picker (DESIGN.md §5, "Model &amp; effort"): the models Claude Code
/// last offered on this machine, Claude Code's own default named rather than left empty, and a choice that needs no
/// question about the cache, since there's none to lose.
/// </summary>
public class UnstartedTabPickerTests
{
    [Fact]
    public async Task A_tab_that_hasnt_started_offers_the_models_last_offered_and_names_the_default()
    {
        await using var h = new TabTestHarness();
        h.Services.State.KnownModels =
        [
            new ModelInfo("opus", "claude-opus-5-5", "Opus", "Opus 5.5 · Best for everyday, complex tasks", true, ["low", "high"]),
            new ModelInfo("haiku", "claude-haiku-5-5", "Haiku", "Haiku 5.5 · Fastest for quick answers", true, ["low"]),
        ];
        Directory.CreateDirectory(h.WorkFolder);
        // The selected tab is shown, which starts it: the other one isn't.
        h.Services.State.Tabs =
        [
            new TabState { Id = "shown", Folder = h.WorkFolder },
            new TabState { Id = "waiting", Folder = h.WorkFolder, SessionId = "s-waiting" },
        ];
        h.Services.State.SelectedTabId = "shown";
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single(t => t.Id == "waiting");

        Assert.Equal(TabStatus.NotStarted, tab.Status);
        Assert.Equal(["opus", "haiku"], tab.Models.Select(m => m.Value));
        Assert.Equal("Default", tab.ModelName);

        // Choosing one doesn't ask about the cached context, which a tab that hasn't started doesn't have.
        InlineDispatcher.Read(() =>
        {
            tab.ChooseModelCommand.Execute(tab.Models[1]);
            return 0;
        });
        Assert.False(tab.ModelSwitch.IsOpen);
    }
}
