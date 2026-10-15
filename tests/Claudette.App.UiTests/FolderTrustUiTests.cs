using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>The card that asks before Claude Code runs a folder's own configuration (DESIGN.md §7, "Folder trust").</summary>
public class FolderTrustUiTests
{
    [AvaloniaFact]
    public async Task The_card_lists_what_the_folder_runs_with_its_choices()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(h.WorkFolder, ".mcp.json"), """{"mcpServers":{"github":{"command":"npx","args":["@acme/github"]}}}""",
            TestContext.Current.CancellationToken);
        var opening = h.Shell.OpenFolderAsync(h.WorkFolder);
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        await UiText.SettleUntilAsync(window, () => h.Shell.SelectedTab?.IsAskingTrust == true, "the question");
        await opening;
        UiText.Settle(window);

        var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Trust this folder?", visible);
        Assert.Contains("MCP server", visible);
        Assert.Contains("github: npx @acme/github", visible);
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
        Assert.Contains("Trust this folder", buttons);
        Assert.Contains("Start without it", buttons);
    }

    [AvaloniaFact]
    public async Task A_list_taller_than_the_window_scrolls_and_leaves_the_choices_on_screen()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, ".claude"));
        var rules = string.Join(",", Enumerable.Range(1, 60).Select(i => $"\"Bash(tool-{i} *)\""));
        await File.WriteAllTextAsync(Path.Combine(h.WorkFolder, ".claude", "settings.local.json"), $$$"""{"permissions":{"allow":[{{{rules}}}]}}""",
            TestContext.Current.CancellationToken);
        var opening = h.Shell.OpenFolderAsync(h.WorkFolder);
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        await UiText.SettleUntilAsync(window, () => h.Shell.SelectedTab?.IsAskingTrust == true, "the question");
        await opening;
        UiText.Settle(window);

        var list = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TrustList");
        Assert.True(list.Extent.Height > list.Viewport.Height, $"the list doesn't scroll ({list.Extent.Height:0} in {list.Viewport.Height:0})");
        foreach (var name in new[] { "Trust this folder", "Start without it" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content as string == name);
            Assert.InRange(button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y, 0, window.ClientSize.Height);
        }
    }
}
