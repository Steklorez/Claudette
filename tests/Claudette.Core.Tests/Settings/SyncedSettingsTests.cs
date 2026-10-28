using System.Text.Json;
using Claudette.Core.Settings;

namespace Claudette.Core.Tests.Settings;

/// <summary>What settings sync copies (DESIGN.md §14, "Settings sync"), which Settings marks every other setting "This computer" by.</summary>
public class SyncedSettingsTests
{
    [Fact]
    public void A_setting_syncs_exactly_when_settings_sync_copies_it()
    {
        var json = JsonSerializer.SerializeToNode(new AppSettings(), JsonFileStore<AppSettings>.Options)!.AsObject();
        var copied = SettingsSync.Flatten(json, SyncedSettings.Sections, SyncedSettings.Leaves).Keys
            .Where(path => !SyncedSettings.PerMachine.Contains(path, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var every = SettingsSync.Flatten(json, json.Select(p => p.Key), SyncedSettings.Leaves).Keys;

        Assert.NotEmpty(copied);
        Assert.All(every, path => Assert.Equal(copied.Contains(path), SyncedSettings.Syncs(path)));
    }

    [Theory]
    [InlineData("appearance.theme", true)]
    [InlineData("appearance.zoom", false)]
    [InlineData("newTabs.defaultModel", true)]
    [InlineData("keyboard.bindings", true)]
    [InlineData("claudeCode.subagents", false)]
    [InlineData("diffTool.kind", false)]
    [InlineData("projectTools.types", false)]
    [InlineData("git.path", false)]
    [InlineData("git.ignoredFiles", false)]
    [InlineData("appearance.showBranchOnTabs", true)]
    [InlineData("general.removeMergedWorktrees", false)]
    [InlineData("state.detailedUsageHeader", false)]
    public void A_path_syncs_by_its_section_but_for_the_few_kept_per_machine(string path, bool syncs) =>
        Assert.Equal(syncs, SyncedSettings.Syncs(path));
}
