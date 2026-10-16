using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.App.Views.Editor;
using Claudette.Core.Settings;

namespace Claudette.App.UiTests;

/// <summary>A project's Project Files tab, rendered (DESIGN.md §18, "Project files").</summary>
public sealed class ProjectFilesUiTests
{
    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The tab's session changed <paramref name="path"/> from <paramref name="before"/> to what's on disk.</summary>
    private static async Task ClaudeEditedAsync(TabTestHarness h, TabViewModel tab, string path, string before)
    {
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use", ["id"] = "e1", ["name"] = "Edit",
                    ["input"] = new JsonObject { ["file_path"] = path, ["old_string"] = "a", ["new_string"] = "b" },
                }),
            },
        });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "e1", ["content"] = "The file has been updated." }) },
            ["tool_use_result"] = new JsonObject { ["filePath"] = path, ["oldString"] = "a", ["newString"] = "b", ["originalFile"] = before },
        });
        await TabTestHarness.Eventually(() => tab.ChangedFiles.HasClaudeChange(path), "the change recorded");
    }

    /// <summary>A starfall-like project: a folder, a file at the top, and Player.cpp, which Claude changed.</summary>
    private static async Task<(TabViewModel Tab, ProjectFilesViewModel Files, string Player, Window Window)> ShowAsync(TabTestHarness h, double width = 1400)
    {
        var tab = await h.OpenTabAsync();
        var player = Write(Path.Combine(h.WorkFolder, "Source", "Player.cpp"),
            "#include \"Player.h\"\n\nvoid APlayer::Dash()\n{\n    if (!HasAuthority())\n    {\n        return;\n    }\n    Spend(DashCost);\n}\n");
        Write(Path.Combine(h.WorkFolder, "Starfall.uproject"), "{}\n");
        await ClaudeEditedAsync(h, tab, player, "#include \"Player.h\"\n\nvoid APlayer::Dash()\n{\n    Spend(DashCost);\n}\n");
        h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single());
        var files = h.Shell.Groups.Single().Files!;
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, width, 800);
        await UiText.SettleUntilAsync(window, () => files.Rows.Count == 2, "the explorer");
        var file = await files.OpenFileAsync(player, preview: false);
        h.Time.Advance(ProjectFilesViewModel.ChangesDelay);
        await UiText.SettleUntilAsync(window, () => file.HasChanges && file.CanReview, "Claude's change");
        return (tab, files, player, window);
    }

    private static ProjectFilesView View(Window window) => window.GetVisualDescendants().OfType<ProjectFilesView>().Single(v => v.IsEffectivelyVisible);

    private static TextEditor Editor(Window window) =>
        window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.IsEffectivelyVisible).GetVisualDescendants().OfType<TextEditor>().First();

    [AvaloniaFact]
    public async Task The_tab_shows_the_explorer_the_files_tabs_and_the_editor_with_Claudes_change_and_the_chip()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (_, files, player, window) = await ShowAsync(h);

        var editor = Editor(window);
        Assert.Equal(File.ReadAllText(player), editor.Document.Text);
        Assert.False(editor.IsReadOnly);
        // The bars beside the line numbers, and the explorer on the left by default.
        Assert.Single(editor.TextArea.LeftMargins.OfType<ChangeMargin>());
        var explorer = View(window).GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "TreeList");
        Assert.True(explorer.TranslatePoint(default, window)!.Value.X < editor.TranslatePoint(default, window)!.Value.X);

        var shown = UiText.Describe(View(window), (h.WorkFolder, "{work}"));
        Assert.Contains("\"Ln 1, Col 1\"", shown, StringComparison.Ordinal);
        Assert.Contains("\"C++\"", shown, StringComparison.Ordinal);
        window.Close();
        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task A_click_on_a_changes_bar_shows_what_was_there_under_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (_, files, _, window) = await ShowAsync(h);
        var file = files.Showing!;
        var block = Assert.Single(file.Markers.Blocks);
        Assert.Equal((5, 4), (block.FirstLine, block.LineCount));

        file.TogglePeekAt(6);
        UiText.Settle(window);

        var peek = window.GetVisualDescendants().OfType<ChangePeek>().Single(p => p.IsEffectivelyVisible);
        var shown = UiText.Describe(peek);
        Assert.Contains("These lines are new", shown, StringComparison.Ordinal);
        // Under the change's last line, the width of the text.
        var editor = Editor(window);
        var lastLine = editor.TextArea.TextView.GetVisualLine(8)!;
        var peekTop = peek.TranslatePoint(default, editor.TextArea.TextView)!.Value.Y;
        Assert.True(peekTop >= lastLine.VisualTop + editor.TextArea.TextView.DefaultLineHeight - editor.TextArea.TextView.VerticalOffset - 1,
            $"The peek starts at {peekTop}, above the end of line 8.");

        file.ClosePeekCommand.Execute(null);
        UiText.Settle(window);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ChangePeek>(), p => p.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Inline_changes_show_Claudes_change_read_only_with_Revert()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (_, files, _, window) = await ShowAsync(h);
        var file = files.Showing!;

        file.Mode = Core.ProjectFiles.FileViewMode.Inline;
        await UiText.SettleUntilAsync(window, () => file.Diff is { IsLoading: false }, "the diff");

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<CodeEditor>(), e => e.IsEffectivelyVisible);
        var shown = UiText.Describe(View(window));
        Assert.Contains("[button] Revert", shown, StringComparison.Ordinal);
        Assert.Contains("[toggle] Whole file", shown, StringComparison.Ordinal);
        Assert.Contains("[toggle on] Inline changes", shown, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task With_Settings_so_the_explorer_is_on_the_right()
    {
        await using var h = new TabTestHarness(s => s.Editor.ExplorerSide = ExplorerSide.Right, dispatcher: new AvaloniaUiDispatcher());
        var (_, _, _, window) = await ShowAsync(h);

        var explorer = View(window).GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "TreeList");
        Assert.True(explorer.TranslatePoint(default, window)!.Value.X > Editor(window).TranslatePoint(default, window)!.Value.X);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_group_has_its_Files_row_first_and_its_button_beside_new_tab()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1400, 800);
        var sidebar = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "Sidebar");
        Assert.Contains("[button] Project files", UiText.Describe(sidebar), StringComparison.Ordinal);

        h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single());
        UiText.Settle(window);

        var shown = UiText.Describe(sidebar).Split('\n');
        var files = Array.FindIndex(shown, l => l.Contains("\"Project Files\"", StringComparison.Ordinal));
        var tab = Array.FindIndex(shown, l => l.Contains(h.Shell.AllTabs.Single().DisplayName, StringComparison.Ordinal) && l.Contains('"'));
        Assert.True(files >= 0 && files < tab, string.Join('\n', shown));
        window.Close();
    }
}
