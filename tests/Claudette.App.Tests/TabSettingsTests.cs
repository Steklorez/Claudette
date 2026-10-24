using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;

namespace Claudette.App.Tests;

/// <summary>
/// Tab settings… (DESIGN.md §14, "Per-tab overrides"): each change applies as it's made, as in Settings, and each
/// section's <b>Use defaults</b> puts only its own back.
/// </summary>
public class TabSettingsTests
{
    [Fact]
    public async Task Each_change_applies_as_it_is_made()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var settings = new TabSettingsViewModel(h.Services, tab, () => { });
        Assert.False(settings.FindSetting("tab.processMonitor")!.IsChanged);

        settings.SelectedMonitor = settings.MonitorChoices.Single(c => c.Label == "On");

        Assert.True(tab.State.Overrides.ShowProcessMonitor);
        await settings.Applying;
        Assert.True(tab.HasOverrides);
        Assert.True(settings.FindSetting("tab.processMonitor")!.IsChanged);
        Assert.True(settings.FindGroup("monitor")!.HasChanges);
    }

    [Fact]
    public async Task Each_sections_Use_defaults_puts_only_its_own_back()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var settings = new TabSettingsViewModel(h.Services, tab, () => { });
        settings.SelectedMonitor = settings.MonitorChoices.Single(c => c.Label == "On");
        settings.SelectedSubagents = settings.SubagentChoices.Single(c => c.Label == "Don't allow them");
        await settings.Applying;

        settings.FindGroup("monitor")!.ResetCommand.Execute(null);
        await settings.Applying;

        Assert.Null(tab.State.Overrides.ShowProcessMonitor);
        Assert.NotNull(tab.State.Overrides.Subagents);
        Assert.Equal(settings.MonitorChoices[0], settings.SelectedMonitor);
        Assert.False(settings.FindGroup("monitor")!.HasChanges);
        Assert.True(settings.FindGroup("model")!.HasChanges);
        // The tab's own switches aren't overrides: their section has nothing to put back.
        Assert.False(settings.FindGroup("thisTab")!.CanReset);
    }

    [Fact]
    public async Task Done_closes_it_with_everything_applied()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var closed = false;
        var settings = new TabSettingsViewModel(h.Services, tab, () => closed = true);
        settings.SelectedAutoContinue = settings.AutoContinueChoices.Single(c => c.Label == "Off");

        await settings.DoneCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.False(tab.State.Overrides.ContinueAfterLimitReset);
    }
}
