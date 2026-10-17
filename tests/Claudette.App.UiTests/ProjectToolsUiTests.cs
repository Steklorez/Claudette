using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;
using Claudette.Core.Settings;

namespace Claudette.App.UiTests;

/// <summary>
/// Project tools rendered (DESIGN.md §18): the project's row at the sidebar's foot and its menu, with the folder's
/// links, the runs under a tab's row in the sidebar, and a run's log in the main area.
/// </summary>
public class ProjectToolsUiTests
{
    [AvaloniaFact]
    public async Task A_tab_in_an_Unreal_project_shows_the_project_at_the_sidebars_foot_and_its_menu_lists_the_actions()
    {
        await using var h = new TabTestHarness(settings => settings.ProjectTools.ProjectFileFormat = ProjectFileFormat.VisualStudio, dispatcher: new AvaloniaUiDispatcher());
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await UiText.SettleUntilAsync(window, () => tab.ProjectTools.Project is not null, "the project");

        // The project's row sits at the sidebar's foot, for the selected tab; the composer's bar has no project chip.
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Project tools");
        Assert.Equal("ProjectButton", button.Name);
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("[button] Project tools: NightOwl · UE 5.4", UiText.Describe(button).Trim());
        Assert.Null(button.FindAncestorOfType<TabView>());

        var flyout = Assert.IsType<Flyout>(button.Flyout);
        flyout.ShowAt(button);
        UiText.Settle(window);
        var menu = Assert.IsAssignableFrom<Control>(flyout.Content);
        await UiText.SettleUntilAsync(window, () => menu.IsEffectivelyVisible, "the menu");
        var shown = UiText.Describe(menu, (h.Root, "{root}"));
        var openSolution = menu.GetVisualDescendants().OfType<Button>().Single(b => UiText.Describe(b).Contains("Open solution", StringComparison.Ordinal));
        Assert.False(openSolution.IsEffectivelyEnabled);
        Assert.Equal("Generate project files first", ToolTip.GetTip(openSolution));
        flyout.Hide();

        // The tab's menu leaves the project to the row: it has no project entries.
        var row = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("tab") && b.DataContext == tab);
        var headers = row.ContextMenu!.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Contains("Tab settings…", headers);
        Assert.DoesNotContain("NightOwl (UE 5.4)", headers);
        Assert.DoesNotContain("Add an action…", headers);

        // Verify resumes off the UI thread, so it comes last.
        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task A_tabs_runs_are_listed_under_its_row_with_Stop_while_running_and_close_once_done()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), launcher: launcher);
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await UiText.SettleUntilAsync(window, () => tab.ProjectTools.Project is not null, "the project");
        var list = window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Classes.Contains("runs"));
        Assert.False(list.IsEffectivelyVisible);

        // One that failed, then one that's running.
        await tab.ProjectTools.RunActionCommand.ExecuteAsync(tab.ProjectTools.Actions.Single(a => a.Id == "generate-project-files"));
        launcher.Processes.Last().WriteOutput("Generating...");
        launcher.Processes.Last().Exit(6);
        var failed = tab.ProjectTools.Runs.Items.Single();
        await UiText.SettleUntilAsync(window, () => failed.Failed, "the failure");
        await tab.ProjectTools.RunActionCommand.ExecuteAsync(tab.ProjectTools.Actions.Single(a => a.Id == "build-editor"));
        var running = tab.ProjectTools.Runs.Items[^1];
        UiText.Settle(window);

        Assert.True(list.IsEffectivelyVisible);
        var shown = UiText.Describe(list);
        var rows = list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("runrow")).ToList();
        Assert.Equal([failed, running], rows.Select(r => r.DataContext));
        // Stop takes the close button's place while a job runs, so a stray click never ends a build.
        static Button Visible(Button row, string name) =>
            row.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == name);
        Assert.DoesNotContain(rows[1].GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Close");
        Assert.Equal("Stop: ends the job and every process it started", ToolTip.GetTip(Visible(rows[1], "Stop")));
        Assert.Equal("Build editor is running…\nStarted 12:00.", ToolTip.GetTip(rows[1]));
        // The detail, under the name, is muted, or in the error color for a failure.
        TextBlock Detail(Button row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("rundetail"));
        Assert.True(Application.Current!.TryGetResource("MutedTextBrush", window.ActualThemeVariant, out var muted));
        Assert.True(Application.Current.TryGetResource("ErrorTextBrush", window.ActualThemeVariant, out var error));
        Assert.Equal(((ISolidColorBrush)muted!).Color, ((ISolidColorBrush)Detail(rows[1]).Foreground!).Color);
        Assert.Equal(((ISolidColorBrush)error!).Color, ((ISolidColorBrush)Detail(rows[0]).Foreground!).Color);

        // A click on an entry shows its log in the main area, in the conversation's place, with no composer.
        rows[0].Command!.Execute(null);
        UiText.Settle(window);
        Assert.True(failed.IsSelected);
        Assert.Contains("selected", rows[0].Classes);
        var view = Shown<ProjectRunView>(window);
        Assert.Same(failed, view.DataContext);
        Assert.Equal("Generate project files failed (exit code 6).", view.FindControl<SelectableTextBlock>("RunStatus")!.Text);
        Assert.Contains("Generating...", Log(view).Document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComposerView>(), c => c.IsEffectivelyVisible);
        // The tab's own row goes back to the conversation.
        window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("tab") && b.DataContext == tab).Command!.Execute(tab);
        UiText.Settle(window);
        Assert.False(view.IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<ComposerView>(), c => c.IsEffectivelyVisible);
        // The actions are in the project's menu at the sidebar's foot, and the logs in the main area, not on the page.
        tab.OpenSidePanelPage(ViewModels.SidePanelPage.Project);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<ScrollViewer>().Single(p => p.Name == "ProjectPage");
        Assert.True(page.IsEffectivelyVisible);
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => tab.ProjectTools.Actions.Any(a => Equals(b.Content, a.Label)));
        Assert.Empty(page.GetVisualDescendants().OfType<TextEditor>());

        // Stop, and the running one becomes a closable entry; closing takes the entry away.
        Visible(rows[1], "Stop").Command!.Execute(null);
        await UiText.SettleUntilAsync(window, () => running.IsStopped, "the stop");
        Visible(rows[1], "Close").Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal([failed], list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("runrow")).Select(r => r.DataContext));

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task A_long_log_follows_its_newest_line_while_the_oldest_are_dropped()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), launcher: launcher);
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await UiText.SettleUntilAsync(window, () => tab.ProjectTools.Project is not null, "the project");
        await tab.ProjectTools.RunActionCommand.ExecuteAsync(tab.ProjectTools.Actions.Single(a => a.Id == "build-editor"));
        var run = tab.ProjectTools.Runs.Items.Single();
        run.OpenCommand.Execute(null);
        UiText.Settle(window);
        var log = Log(Shown<ProjectRunView>(window));

        // In batches, as a busy build writes them, past the limit more than once.
        var process = launcher.Processes.Last();
        for (var i = 1; i <= 6200; i++)
        {
            process.WriteOutput($"line {i}");
            if (i % 400 == 0)
            {
                await UiText.SettleUntilAsync(window, () => run.Output[^1] == $"line {i}", "the batch");
            }
        }
        await UiText.SettleUntilAsync(window, () => run.Output[^1] == "line 6200", "the output");
        UiText.Settle(window);

        // The editor keeps step with the run's lines, the oldest dropped from both.
        Assert.Equal(1500, run.OutputDropped);
        Assert.Equal(string.Join("\n", run.Output), log.Document.Text);
        // It follows the newest line, as a terminal does.
        var view = log.TextArea.TextView;
        Assert.True(view.VisualLinesValid);
        Assert.Equal(log.Document.LineCount, view.VisualLines[^1].FirstDocumentLine.LineNumber);
        Assert.DoesNotContain(view.VisualLines, l => l.FirstDocumentLine.LineNumber == 1);
    }

    [AvaloniaFact]
    public async Task A_logs_long_lines_wrap_and_its_links_open_with_a_click()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), launcher: launcher);
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await UiText.SettleUntilAsync(window, () => tab.ProjectTools.Project is not null, "the project");
        await tab.ProjectTools.RunActionCommand.ExecuteAsync(tab.ProjectTools.Actions.Single(a => a.Id == "build-editor"));
        var run = tab.ProjectTools.Runs.Items.Single();
        run.OpenCommand.Execute(null);
        var process = launcher.Processes.Last();
        process.WriteOutput("See https://dev.epicgames.com/documentation for help.");
        process.WriteOutput("error C2065: very_long_" + string.Concat(Enumerable.Repeat("unbreakable_", 30)) + ": undeclared identifier");
        await UiText.SettleUntilAsync(window, () => run.Output.Count == 3, "the output");
        var log = Log(Shown<ProjectRunView>(window));
        var textView = log.TextArea.TextView;
        await UiText.SettleUntilAsync(window, () => textView.VisualLinesValid && textView.VisualLines.Count == 3, "the lines");

        // Long lines wrap at the width shown: nothing scrolls sideways.
        Assert.True(textView.VisualLines[2].TextLines.Count > 1, "the long line wraps");
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, log.HorizontalScrollBarVisibility);

        // The address is a link; a click on it opens it.
        var line = textView.VisualLines[1];
        Assert.Contains(line.Elements, e => e.GetType().Name == "LinkText");
        var start = "See ".Length + 4;
        var at = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(2, start), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - textView.ScrollOffset;
        var point = textView.TranslatePoint(at, window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        await UiText.SettleUntilAsync(window, () => h.Platform.OpenedUrls.Count == 1, "the link to open");
        Assert.Equal(["https://dev.epicgames.com/documentation"], h.Platform.OpenedUrls);
        process.Exit(0);
    }

    /// <summary>The one <typeparamref name="T"/> on screen.</summary>
    private static T Shown<T>(Window window) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible);

    /// <summary>A run's log, in its view.</summary>
    private static TextEditor Log(ProjectRunView view) => view.FindControl<TextEditor>("Log")!;

    [AvaloniaFact]
    public async Task A_folder_with_no_project_has_the_row_named_after_it_and_its_menu_lists_the_links()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        File.WriteAllText(Path.Combine(h.WorkFolder, Core.ProjectTools.ProjectFile.SharedName), """
            { "links": [
                { "name": "Board", "url": "https://example.atlassian.net/jira/software/projects/ABC/boards/1" },
                { "name": "Pull request", "url": "https://github.com/org/repo/compare/{branch}?expand=1" },
            ] }
            """);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        await UiText.SettleUntilAsync(window, () => tab.ProjectTools.HasLinks, "the links");

        // No provider recognized a project, and there are no actions: the row is there all the same, named after the folder.
        Assert.Null(tab.ProjectTools.Project);
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Project tools");
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal($"[button] Project tools: {tab.FolderName}", UiText.Describe(button).Trim());
        Assert.StartsWith(tab.Folder, ToolTip.GetTip(button) as string, StringComparison.Ordinal);

        var flyout = Assert.IsType<Flyout>(button.Flyout);
        flyout.ShowAt(button);
        UiText.Settle(window);
        var menu = Assert.IsAssignableFrom<Control>(flyout.Content);
        await UiText.SettleUntilAsync(window, () => menu.IsEffectivelyVisible, "the menu");
        var shown = UiText.Describe(menu, (h.Root, "{root}"));
        var buttons = menu.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        var board = buttons.Single(b => UiText.Describe(b).Contains("Board", StringComparison.Ordinal));
        Assert.True(board.IsEffectivelyEnabled);
        Assert.Equal("https://example.atlassian.net/jira/software/projects/ABC/boards/1", ToolTip.GetTip(board));
        var pullRequest = buttons.Single(b => UiText.Describe(b).Contains("Pull request", StringComparison.Ordinal));
        Assert.False(pullRequest.IsEffectivelyEnabled);
        Assert.Equal("No git branch", ToolTip.GetTip(pullRequest));
        // No Project page without project tools, so nothing to show the output of.
        Assert.DoesNotContain(buttons, b => UiText.Describe(b).Contains("Show output…", StringComparison.Ordinal));
        flyout.Hide();

        await Verify(shown);
    }
}
