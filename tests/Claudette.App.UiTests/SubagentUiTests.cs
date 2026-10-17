using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.Core.Settings;

namespace Claudette.App.UiTests;

/// <summary>
/// Subagents' work under the tab rendered (DESIGN.md §5, "Subagents"): their rows in the sidebar, a subagent's work in
/// the tab with the bar in place of the composer, and the conversation's one-line card.
/// </summary>
public class SubagentUiTests
{
    [AvaloniaFact]
    public async Task A_tabs_subagents_sit_under_its_row_the_nested_one_further_in_and_an_earlier_one_dimmed()
    {
        await using var h = new TabTestHarness(InSidebar, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore the auth code", "Find it", "Explore"));
        h.Transport.Emit(Wire.Result("a1", "Tokens are checked in the middleware."));
        h.Transport.Emit(Wire.TurnResult());
        await UiText.SettleUntilAsync(window, () => tab.Agents.Find("a1")?.IsDone == true && tab.Status == TabStatus.Idle, "the first turn");
        tab.ComposerText = "Now fix it";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a2", "Fix the middleware", "Fix it"));
        h.Transport.Emit(Wire.TaskStarted("task-b", "a2"));
        h.Transport.Emit(Wire.Agent("a3", "Check the tests", "Run them", "Explore", parent: "a2"));
        h.Transport.Emit(Wire.Tool("a3", "g1", "Grep", new JsonObject { ["pattern"] = "auth", ["path"] = "tests/" }));
        await UiText.SettleUntilAsync(window, () => tab.SubagentRows.Count == 3 && tab.Agents.Find("a3")?.ToolCalls == 1, "the rows");

        var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("agentrow")).ToList();
        double Left(Control c) => c.TranslatePoint(default, window)!.Value.X;
        double Top(Control c) => c.TranslatePoint(default, window)!.Value.Y;
        var tabRow = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabrow"));

        Assert.Equal(["a1", "a2", "a3"], rows.Select(r => ((SubagentRowViewModel)r.DataContext!).Node.ToolUseId));
        Assert.True(Top(rows[0]) > Top(tabRow), "The rows should be under the tab's");
        Assert.True(Left(rows[0]) > Left(tabRow), "The rows should be indented under the tab's");
        Assert.True(Left(rows[2]) > Left(rows[1]), "The nested subagent should sit further in");
        Assert.Contains("earlier", rows[0].Classes);
        Assert.DoesNotContain("earlier", rows[1].Classes);
        Assert.True(rows[0].Opacity < 1);
        var list = Assert.Single(window.GetVisualDescendants().OfType<ItemsControl>(), c => c.Classes.Contains("subagents"));
        await Verify(UiText.Describe(list));
    }

    [AvaloniaFact]
    public async Task A_subagents_work_shows_in_the_tab_with_a_bar_where_the_composer_was()
    {
        await using var h = new TabTestHarness(InSidebar, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Review the change", "Review the auth change and say what's missing."));
        h.Transport.Emit(Wire.TaskStarted("task-a", "a1"));
        h.Transport.Emit(Wire.Tool("a1", "r1", "Read", new JsonObject { ["file_path"] = "src/auth.ts" }));
        h.Transport.Emit(Wire.Text("a1", "The refresh path never checks expiry."));
        await UiText.SettleUntilAsync(window, () => tab.SubagentRows.Count == 1 && tab.Agents.Find("a1")!.Item!.Items.Count == 2, "the subagent's work");

        tab.SubagentRows[0].OpenCommand.Execute(null);
        await UiText.SettleUntilAsync(window, () => Visible(window, "AgentBar") is not null, "its work");

        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        Assert.False(view.GetVisualDescendants().OfType<ComposerView>().Single().IsEffectivelyVisible);
        Assert.Null(Visible(window, "ConversationScroll"));
        var work = Assert.IsType<ScrollViewer>(Visible(window, "AgentScroll"));
        Assert.Contains("The refresh path never checks expiry.", UiText.Describe(work), StringComparison.Ordinal);
        Assert.Contains("showing", window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("agentrow")).Classes);
        await Verify(UiText.Describe(Visible(window, "AgentHeader")!) + "\n---\n" + UiText.Describe(Visible(window, "AgentBar")!));
    }

    [AvaloniaFact]
    public async Task In_sidebar_mode_a_subagents_card_is_one_line_even_when_it_was_open()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore the auth code", "Find where tokens are checked.", "Explore"));
        h.Transport.Emit(Wire.Text("a1", "Looking in src/auth."));
        await UiText.SettleUntilAsync(window, () => tab.Agents.Find("a1")?.Item?.Items.Count == 1, "the subagent");
        var card = tab.Items.OfType<SubagentItem>().Single();
        tab.OpenSubagentCommand.Execute(card);
        await UiText.SettleUntilAsync(window, () => SubagentCardText(window).Contains("Looking in src/auth.", StringComparison.Ordinal), "the open card");

        h.Services.Settings.Appearance.ShowSubagents = SubagentDisplay.InSidebar;
        h.Services.SaveSettings();
        await UiText.SettleUntilAsync(window, () => !SubagentCardText(window).Contains("Looking in src/auth.", StringComparison.Ordinal), "the one-line card");

        Assert.True(card.IsExpanded);
        Assert.Contains("Explore the auth code", SubagentCardText(window), StringComparison.Ordinal);
        tab.OpenSubagentCommand.Execute(card);
        Assert.True(tab.IsViewingAgent);
    }

    private static void InSidebar(AppSettings settings) => settings.Appearance.ShowSubagents = SubagentDisplay.InSidebar;

    private static Control? Visible(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private static string SubagentCardText(Window window) =>
        UiText.Describe(window.GetVisualDescendants().OfType<Controls.Card>().First(c => c.Classes.Contains("agent")));
}
