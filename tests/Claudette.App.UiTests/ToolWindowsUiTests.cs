using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Claudette.App.Controls;
using Claudette.App.Diffs;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.ViewModels.ToolWindows;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>
/// The tool windows rendered (DESIGN.md §3, "Tool windows"): the right edge's tabs along the window's edge, a window slid
/// out over the conversation, the pin docking them beside it, and what the session's windows show: ticking changed files
/// as reviewed (DESIGN.md §8), and processes (DESIGN.md §4).
/// </summary>
public class ToolWindowsUiTests
{
    /// <summary>The tool window on screen, slid out or docked: where its page's controls are.</summary>
    private static Control ShownWindow(Window window) => window.GetVisualDescendants().OfType<ToolWindowView>().First(v => v.IsEffectivelyVisible);

    [AvaloniaFact]
    public async Task The_right_edges_tabs_run_down_the_window_and_a_click_slides_one_out_over_the_conversation()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        UiText.Settle(window);
        var strip = window.GetVisualDescendants().OfType<ToolStripView>().Single(s => s.IsEffectivelyVisible);
        var tabs = strip.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("striptab")).ToList();
        Assert.Equal(["Changed files, auto-hidden, right", "Agents, auto-hidden, right", "Scratch pad, auto-hidden, right"],
            tabs.Select(AutomationProperties.GetName));
        // Down the window's edge, each under the one before, reading downwards.
        Assert.True(tabs[1].TranslatePoint(default, strip)!.Value.Y > tabs[0].TranslatePoint(default, strip)!.Value.Y);
        Assert.True(tabs[0].Bounds.Height > tabs[0].Bounds.Width);
        var slide = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RightSlide");
        Assert.False(slide.IsVisible);

        tabs[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UiText.Settle(window);

        Assert.True(slide.IsEffectivelyVisible);
        Assert.Equal(ToolDockViewModel.DefaultRightWidth, slide.Bounds.Width);
        Assert.NotNull(ShownWindow(window).GetVisualDescendants().OfType<ListBox>().SingleOrDefault(l => l.Name == "ChangedFilesList"));
        // It lies over the conversation, which keeps its width.
        var conversation = window.GetVisualDescendants().OfType<TabView>().Single();
        Assert.True(conversation.Bounds.Width > ShellLayout.MinConversationWidth + slide.Bounds.Width);
    }

    [AvaloniaFact]
    public async Task The_pin_docks_the_edge_beside_the_conversation_with_its_windows_tabs()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.ShowToolWindow(ToolWindowKinds.Files);
        UiText.Settle(window);
        var pin = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("toolpin") && b.IsEffectivelyVisible);
        Assert.Equal("Dock", AutomationProperties.GetName(pin));

        pin.Command!.Execute(null);
        UiText.Settle(window);

        var edge = window.GetVisualDescendants().OfType<ToolEdgeView>().Single(v => v.Name == "RightEdge");
        Assert.True(edge.IsEffectivelyVisible);
        Assert.Equal(ToolDockViewModel.DefaultRightWidth, edge.Bounds.Width);
        var tabs = edge.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tooltab")).ToList();
        Assert.Equal(["Changed files", "Agents", "Scratch pad"], tabs.Select(t => t.GetVisualDescendants().OfType<TextBlock>().First().Text));
        Assert.Contains("selected", tabs[0].Classes);
        // Beside the conversation now, not over it.
        var conversation = window.GetVisualDescendants().OfType<TabView>().Single();
        var right = conversation.TranslatePoint(new Point(conversation.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= edge.TranslatePoint(default, window)!.Value.X + 1);
        Assert.Equal("Auto-hide", AutomationProperties.GetName(edge.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("toolpin"))));
    }
    [AvaloniaFact]
    public async Task A_changed_files_box_ticks_it_as_reviewed_without_opening_the_diff()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        // The panel's pages are only in the visual tree once it has been laid out open.
        UiText.Settle(window);
        var (first, second) = (Path.Combine(h.WorkFolder, "auth.cs"), Path.Combine(h.WorkFolder, "login.cs"));
        await File.WriteAllTextAsync(first, "b\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "b\n", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", first));
        h.Transport.Emit(EditResult("e1", first));
        h.Transport.Emit(Edit("e2", second));
        h.Transport.Emit(EditResult("e2", second));
        var view = ShownWindow(window);
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");
        await UiText.SettleUntilAsync(window, () => Boxes(list).Count == 2, "the changed files");
        var opened = 0;
        tab.ChangedFiles.DiffRequested += _ => opened++;

        Click(window, Boxes(list)[0]);

        Assert.Equal(0, opened);
        Assert.Null(list.SelectedItem);
        Assert.True(tab.ChangedFiles.Files[0].IsReviewed);
        Assert.Equal([true, false], Boxes(list).Select(b => b.IsChecked == true));
        Assert.Equal("2 files changed · 1 reviewed", tab.ChangedFiles.Summary);
        // A reviewed file is drawn faintly; its box isn't.
        var rows = list.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("changedfile")).ToList();
        Assert.Equal([0.5, 1.0], rows.Select(r => r.Children.OfType<StackPanel>().Single().Opacity));
        Assert.Equal(1.0, Boxes(list)[0].Opacity);

        // Unticked by a click, then ticked from the view model, the box follows.
        Click(window, Boxes(list)[0]);
        Assert.False(tab.ChangedFiles.Files[0].IsReviewed);
        tab.ChangedFiles.ToggleFileReviewedCommand.Execute(tab.ChangedFiles.Files[0]);
        UiText.Settle(window);
        Assert.True(Boxes(list)[0].IsChecked);

        // Claude changing the file again unticks it.
        var panel = window.GetVisualDescendants().OfType<ToolEdgeView>().Single(v => v.Name == "RightEdge");
        var shown = UiText.Describe(panel);
        h.Transport.Emit(Edit("e3", first));
        h.Transport.Emit(EditResult("e3", first));
        await UiText.SettleUntilAsync(window, () => tab.ChangedFiles.Files is [{ LatestChange: "e3" }, _], "Claude's next change");
        Assert.Equal([false, false], Boxes(list).Select(b => b.IsChecked == true));

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task A_file_changed_since_the_sessions_change_says_so_under_its_folder()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        UiText.Settle(window);
        var path = Path.Combine(h.WorkFolder, "auth.cs");
        // Claude's Edit leaves "b"; another line is on disk since.
        await File.WriteAllTextAsync(path, "b\nyours\n", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", path));
        h.Transport.Emit(EditResult("e1", path));
        var view = ShownWindow(window);
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");
        await UiText.SettleUntilAsync(window, () => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Changed since: +1 −0"), "the line for what changed since");

        var since = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Changed since: +1 −0");
        Assert.Contains("warning", since.Classes);
        await Verify(UiText.Describe(list));
    }

    [AvaloniaFact]
    public async Task A_change_the_lines_dont_show_is_said_under_its_folder()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        UiText.Settle(window);
        var (spaces, newline) = (Path.Combine(h.WorkFolder, "auth.cs"), Path.Combine(h.WorkFolder, "login.cs"));
        // One Edit takes a space off the end of a line; the other, the newline off the end of the file.
        await File.WriteAllTextAsync(spaces, "a\nb\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(newline, "a\nb", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", spaces, "a ", "a"));
        h.Transport.Emit(EditResult("e1", spaces, "a ", "a", "a \nb\n"));
        h.Transport.Emit(Edit("e2", newline, "b\n", "b"));
        h.Transport.Emit(EditResult("e2", newline, "b\n", "b", "a\nb\n"));
        var view = ShownWindow(window);
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");

        await UiText.SettleUntilAsync(window, () => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Only the newline at its end changed: removed"),
            "the line for what the lines don't show");

        var note = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Only whitespace changed");
        Assert.Contains("muted", note.Classes);
        await Verify(UiText.Describe(list));
    }

    [AvaloniaFact]
    public async Task Changed_files_in_another_working_tree_are_headed_by_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        UiText.Settle(window);
        var (home, there) = (Path.Combine(h.WorkFolder, "auth.cs"), Path.Combine(worktree, "login.cs"));
        await File.WriteAllTextAsync(home, "b\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(there, "b\n", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", home));
        h.Transport.Emit(EditResult("e1", home));
        h.Transport.Emit(Edit("e2", there));
        h.Transport.Emit(EditResult("e2", there));
        var view = ShownWindow(window);
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");

        await UiText.SettleUntilAsync(window, () => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "brisk-otter · worktree-brisk-otter" && t.IsEffectivelyVisible),
            "the worktree's heading");

        var heading = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "brisk-otter · worktree-brisk-otter");
        Assert.Contains("changedgroup", heading.Classes);
        await Verify(UiText.Describe(list));
    }

    [AvaloniaFact]
    public async Task The_arrow_keys_move_through_changed_files_without_opening_them()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        // The panel's pages are only in the visual tree once it has been laid out open.
        UiText.Settle(window);
        var (first, second) = (Path.Combine(h.WorkFolder, "auth.cs"), Path.Combine(h.WorkFolder, "login.cs"));
        await File.WriteAllTextAsync(first, "b\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "b\n", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", first));
        h.Transport.Emit(EditResult("e1", first));
        h.Transport.Emit(Edit("e2", second));
        h.Transport.Emit(EditResult("e2", second));
        var view = ShownWindow(window);
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");
        await UiText.SettleUntilAsync(window, () => Boxes(list).Count == 2, "the changed files");
        var opened = new List<string>();
        tab.ChangedFiles.DiffRequested += source => opened.Add(Path.GetFileName(source.Path));

        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal(1, list.SelectedIndex);
        Assert.Empty(opened);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Equal(["login.cs"], opened);

        // A click opens a file, the same one again too.
        var name = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "auth.cs");
        Click(window, name);
        Click(window, name);
        Assert.Equal(["login.cs", "auth.cs", "auth.cs"], opened);
    }

    [AvaloniaFact]
    public async Task Reviewed_in_the_diff_window_ticks_the_file_and_closes_the_window()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"claudette-diff-{Guid.NewGuid():N}");
        var file = Path.Combine(folder, "auth.cs");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(file, "one\ntwo\n", TestContext.Current.CancellationToken);
        try
        {
            string? marked = "none";
            var source = new DiffSource(file, "src/auth.cs", "one\n", "compared with before Claude's first change in this session",
                OpenInDiffTool: null, () => Task.FromResult(false), () => Task.CompletedTask, () => Task.CompletedTask,
                Review: new DiffReview(() => "e1", () => false, change => marked = change));
            var window = new DiffWindow { DataContext = new DiffWindowViewModel(source, dark: false) };
            window.Show();
            var reviewed = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReviewedButton");
            await UiText.SettleUntilAsync(window, () => ((DiffWindowViewModel)window.DataContext!).IsLoading == false, "the diff");
            var header = reviewed.FindAncestorOfType<Border>()!;
            var shown = UiText.Describe(header);
            var closed = false;
            window.Closed += (_, _) => closed = true;

            reviewed.Command!.Execute(null);
            UiText.Settle(window);

            Assert.Equal("e1", marked);
            Assert.True(closed);
            await Verify(shown);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_process_row_lights_up_under_the_pointer_and_while_its_menu_is_open()
    {
        await using var h = new TabTestHarness(s => s.Processes.ShowMonitor = true, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Processes);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        // The panel's pages are only in the visual tree once it has been laid out open.
        UiText.Settle(window);
        var view = ShownWindow(window);
        List<Border> Rows() => [.. view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("processrow"))];
        await UiText.SettleUntilAsync(window, () => Rows().Count > 0, "the processes");
        var row = Rows()[0];
        var hover = Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource(window.ActualThemeVariant, "HoverBrush")).Color;
        Assert.Equal(Colors.Transparent, Fill(row));

        window.MouseMove(row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value);
        UiText.Settle(window);
        Assert.Equal(hover, Fill(row));

        window.MouseMove(new Point(5, 5));
        UiText.Settle(window);
        Assert.Equal(Colors.Transparent, Fill(row));

        // Its menu open, the row stays lit, so it's plain which process the menu is for.
        row.ContextMenu!.Open(row);
        UiText.Settle(window);
        Assert.Equal(hover, Fill(row));
        row.ContextMenu.Close();
        UiText.Settle(window);
        Assert.Equal(Colors.Transparent, Fill(row));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_process_with_children_has_an_arrow_that_collapses_them_and_Alt_click_takes_every_level()
    {
        await using var h = new TabTestHarness(s => s.Processes.ShowMonitor = true, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        // claude → bash → node → esbuild, and rg beside bash.
        var tree = h.Trees.Trees[4242];
        tree.Children.AddRange([(5001, "bash"), (5002, "node"), (5003, "esbuild"), (5004, "rg")]);
        tree.Parents[5002] = 5001;
        tree.Parents[5003] = 5002;
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Processes);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        // The panel's pages are only in the visual tree once it has been laid out open.
        UiText.Settle(window);
        var view = ShownWindow(window);
        Button Arrow(string name) => view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("processfold") && b.DataContext is ProcessRow row && row.Name == name);
        IEnumerable<string> Shown() => tab.ProcessMonitor.Processes.Select(p => p.Name);
        await UiText.SettleUntilAsync(window, () => view.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("processfold")) == 4, "the processes");

        // node, two levels under claude, starts collapsed; rg has nothing under it, so no arrow.
        Assert.Equal(["claude", "bash", "node", "rg"], Shown());
        Assert.True(Arrow("bash").IsEffectivelyVisible);
        Assert.Equal("Expand", AutomationProperties.GetName(Arrow("node")));
        Assert.False(Arrow("rg").IsEffectivelyVisible);

        Click(window, Arrow("bash"));
        Assert.Equal(["claude", "bash", "rg"], Shown());
        Assert.Equal("Expand", AutomationProperties.GetName(Arrow("bash")));

        // Alt+click opens every level under it, node's too.
        var at = Arrow("bash").TranslatePoint(new Point(Arrow("bash").Bounds.Width / 2, 6), window)!.Value;
        window.MouseMove(at, RawInputModifiers.Alt);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Alt);
        UiText.Settle(window);
        Assert.Equal(["claude", "bash", "node", "esbuild", "rg"], Shown());
        Assert.Equal("Collapse", AutomationProperties.GetName(Arrow("node")));
        window.Close();
    }

    private static Color? Fill(Border border) => (border.Background as ISolidColorBrush)?.Color;

    private static List<CheckBox> Boxes(ListBox list) =>
        list.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Classes.Contains("reviewed")).ToList();

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        UiText.Settle(window);
    }

    [AvaloniaFact]
    public async Task A_changed_files_letter_and_counts_and_a_tool_tiles_counts_sit_on_their_names_baseline_at_any_code_size()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (window, row) = await ShowChangedFileAsync(h);
        var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FileName");
        var aligned = row.GetVisualDescendants().OfType<TextBlock>().Where(t => Baseline.GetAlignWith(t) == name).ToList();
        // An Edit's tile has its counts beside its state (DESIGN.md §18, "Conversation cards").
        var tool = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "TileState" && t.DataContext is Conversation.ToolUseItem { Name: "Edit" });
        var summary = window.GetVisualDescendants().OfType<TextBlock>().Where(t => Baseline.GetAlignWith(t) == tool && t.IsEffectivelyVisible).ToList();

        // The status letter and the counts; the tile's counts.
        Assert.Equal(2, aligned.Count);
        Assert.Single(summary);
        void AllOnBaseline()
        {
            UiText.Settle(window);
            Assert.All(aligned, text => BaselineTests.AssertOnBaseline(text, name, window));
            Assert.All(summary, text => BaselineTests.AssertOnBaseline(text, tool, window));
        }
        AllOnBaseline();
        var resources = Avalonia.Application.Current!.Resources;
        var size = resources["CodeFontSize"];
        try
        {
            resources["CodeFontSize"] = 22.0;
            AllOnBaseline();
            resources["CodeFontSize"] = 9.0;
            AllOnBaseline();
        }
        finally
        {
            resources["CodeFontSize"] = size;
        }
    }

    [AvaloniaFact]
    public async Task A_changed_files_letter_sits_midway_between_its_box_and_its_name_at_any_code_size()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (window, row) = await ShowChangedFileAsync(h);
        var box = row.Children.OfType<CheckBox>().Single();
        var letter = row.Children.OfType<TextBlock>().Single();
        var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FileName");

        void Midway()
        {
            UiText.Settle(window);
            var before = letter.Bounds.Left - box.Bounds.Right;
            var after = name.TranslatePoint(default, row)!.Value.X - letter.Bounds.Right;
            Assert.True(before > 0, $"The letter touches the box ({before}).");
            Assert.Equal(before, after, 0.5);
        }
        Midway();
        var resources = Avalonia.Application.Current!.Resources;
        var size = resources["CodeFontSize"];
        try
        {
            resources["CodeFontSize"] = 28.0;
            Midway();
            resources["CodeFontSize"] = 8.0;
            Midway();
        }
        finally
        {
            resources["CodeFontSize"] = size;
        }
    }

    /// <summary>A tab with its side panel open on one file Claude edited, and that file's row.</summary>
    private static async Task<(Window Window, Grid Row)> ShowChangedFileAsync(TabTestHarness h)
    {
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.DockToolWindow(ToolWindowKinds.Files);
        UiText.Settle(window);
        var file = Path.Combine(h.WorkFolder, "auth.cs");
        await File.WriteAllTextAsync(file, "b\n", TestContext.Current.CancellationToken);
        h.Transport.Emit(Edit("e1", file));
        h.Transport.Emit(EditResult("e1", file));
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChangedFilesList");
        await UiText.SettleUntilAsync(window, () => Boxes(list).Count == 1, "the changed file");
        return (window, list.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("changedfile")));
    }

    private static JsonObject Edit(string id, string path, string from = "a", string to = "b") => new()
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use", ["id"] = id, ["name"] = "Edit", ["input"] = new JsonObject { ["file_path"] = path, ["old_string"] = from, ["new_string"] = to },
            }),
        },
    };

    private static JsonObject EditResult(string id, string path, string from = "a", string to = "b", string originalFile = "a\n") => new()
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = "The file has been updated." }) },
        ["tool_use_result"] = new JsonObject { ["filePath"] = path, ["oldString"] = from, ["newString"] = to, ["originalFile"] = originalFile },
    };

}
