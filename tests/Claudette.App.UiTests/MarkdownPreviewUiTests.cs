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
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>A Markdown file's preview in a Project Files tab, rendered (DESIGN.md §18, "Markdown preview").</summary>
public sealed class MarkdownPreviewUiTests
{
    private const string Readme = "---\ntitle: Starfall\n---\n# Starfall\n\nA space shooter for **two** players.\n\n## Building\n\n1. Open `Starfall.uproject`.\n2. Build the editor.\n\n```bash\ndotnet build\n```\n\nWrapped in the file\nonto two lines.\n";

    /// <summary>
    /// A heading, then a hundred and twenty paragraphs (paragraph <c>n</c> is on line <c>2n + 1</c>), and a heading with
    /// enough after it to scroll it to the top.
    /// </summary>
    private static readonly string Long = "# Long\n\n" + string.Concat(Enumerable.Range(1, 120).Select(i => $"Paragraph {i}.\n\n"))
        + "## Saving\n\n" + string.Concat(Enumerable.Range(1, 40).Select(i => $"More {i}.\n\n"));

    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static async Task<(ProjectFileViewModel File, Window Window)> ShowAsync(TabTestHarness h, string name, string text)
    {
        await h.OpenTabAsync();
        var path = Write(Path.Combine(h.WorkFolder, name), text);
        h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single());
        var files = h.Shell.Groups.Single().Files!;
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1400, 800);
        var file = await files.OpenFileAsync(path, preview: false);
        await UiText.SettleUntilAsync(window, () => file.IsLoaded, "the file");
        return (file, window);
    }

    private static ProjectFileView View(Window window) => window.GetVisualDescendants().OfType<ProjectFileView>().Single(v => v.IsEffectivelyVisible);

    private static MarkdownPreviewView Preview(Window window) => View(window).GetVisualDescendants().OfType<MarkdownPreviewView>().Single();

    private static TextEditor Editor(Window window) => View(window).GetVisualDescendants().OfType<TextEditor>().First();

    /// <summary>The preview's block of text that starts with <paramref name="text"/>, once it's there.</summary>
    private static MarkdownTextBlock? Block(Window window, string text) =>
        Preview(window).GetVisualDescendants().OfType<MarkdownTextBlock>().FirstOrDefault(b => b.IsEffectivelyVisible && b.LayoutText.StartsWith(text, StringComparison.Ordinal));

    /// <summary>How far below the top of the preview's view <paramref name="block"/> is.</summary>
    private static double FromTop(Window window, Control block) =>
        block.TranslatePoint(default, Preview(window).GetVisualDescendants().OfType<ScrollViewer>().First())!.Value.Y;

    private static ScrollViewer Scroller(Window window) => Preview(window).GetVisualDescendants().OfType<ScrollViewer>().First();

    [AvaloniaFact]
    public async Task A_markdown_files_bar_offers_Edit_Preview_and_Edit_and_preview_and_Preview_shows_it_rendered()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (file, window) = await ShowAsync(h, "README.md", Readme);

        var bar = UiText.Describe(View(window).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modebar")));
        Assert.Contains("[toggle on] Edit", bar, StringComparison.Ordinal);
        Assert.Contains("[toggle] Preview", bar, StringComparison.Ordinal);
        Assert.Contains("[toggle] Edit and preview", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("Inline changes", bar, StringComparison.Ordinal);

        file.IsPreviewMode = true;
        await UiText.SettleUntilAsync(window, () => Block(window, "Starfall") is not null, "the preview");

        Assert.False(Editor(window).IsEffectivelyVisible);
        // The front matter shows nothing; the heading, list and code do.
        Assert.Null(Block(window, "title"));
        Assert.NotNull(Block(window, "Open Starfall.uproject."));
        Assert.NotNull(Block(window, "dotnet build"));
        // A paragraph wrapped in the file is one line, as VS Code and GitHub show it.
        Assert.Equal("Wrapped in the file onto two lines.", Block(window, "Wrapped")!.LayoutText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Edit_and_preview_has_the_editor_on_the_left_and_the_preview_on_the_right_half_the_width_each()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (file, window) = await ShowAsync(h, "README.md", Readme);

        file.IsEditAndPreviewMode = true;
        await UiText.SettleUntilAsync(window, () => Block(window, "Starfall") is not null, "the preview");

        var editor = Editor(window);
        var preview = Preview(window);
        Assert.True(editor.IsEffectivelyVisible);
        var editorLeft = editor.TranslatePoint(default, window)!.Value.X;
        var previewLeft = preview.TranslatePoint(default, window)!.Value.X;
        Assert.True(editorLeft < previewLeft);
        Assert.InRange(preview.Bounds.Width - editor.Bounds.Width, -10, 10);

        // Typing shows in the preview a moment later.
        editor.Document.Insert(editor.Document.GetLineByNumber(6).Offset, "Typed. ");
        h.Time.Advance(ProjectFileViewModel.MarkersDelay);
        await UiText.SettleUntilAsync(window, () => Block(window, "Typed. A space shooter") is not null, "the typing in the preview");
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_editor_and_the_preview_scroll_together_and_keep_the_place_between_the_modes()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (file, window) = await ShowAsync(h, "LONG.md", Long);
        file.IsEditAndPreviewMode = true;
        await UiText.SettleUntilAsync(window, () => Block(window, "Paragraph 120.") is not null, "the preview");
        var editor = Editor(window);
        var view = editor.TextArea.TextView;

        // The editor scrolled to paragraph 100's line: the preview has it at its top.
        View(window).GetVisualDescendants().OfType<CodeEditor>().Single().ScrollToVerticalOffset(view.GetVisualTopByDocumentLine(201));
        await UiText.SettleUntilAsync(window, () => Math.Abs(FromTop(window, Block(window, "Paragraph 100.")!)) < 3, "the preview to follow");

        // The preview scrolled to paragraph 50: the editor has its line at its top.
        var scroller = Scroller(window);
        scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + FromTop(window, Block(window, "Paragraph 50.")!));
        await UiText.SettleUntilAsync(window, () => view.GetDocumentLineByVisualTop(view.ScrollOffset.Y)?.LineNumber == 101, "the editor to follow");

        // Preview alone, then Edit, keep paragraph 50 at the top.
        file.IsPreviewMode = true;
        await UiText.SettleUntilAsync(window, () => Math.Abs(FromTop(window, Block(window, "Paragraph 50.")!)) < 3, "the preview's place");
        file.IsEditMode = true;
        await UiText.SettleUntilAsync(window, () => view.GetDocumentLineByVisualTop(view.ScrollOffset.Y)?.LineNumber == 101, "the editor's place");
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_link_to_a_heading_scrolls_the_preview_to_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var (file, window) = await ShowAsync(h, "LONG.md", Long);
        file.IsPreviewMode = true;
        await UiText.SettleUntilAsync(window, () => Block(window, "Saving") is not null, "the preview");

        await file.OpenLinkCommand.ExecuteAsync("#saving");

        await UiText.SettleUntilAsync(window, () => Math.Abs(FromTop(window, Block(window, "Saving")!)) < 3, "the heading at the top");
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_image_in_the_files_folder_shows_read_against_its_folder()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var png = Path.Combine(h.WorkFolder, "docs", "screenshots", "menu.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await File.WriteAllBytesAsync(png, TestImages.Png, TestContext.Current.CancellationToken);
        var (file, window) = await ShowAsync(h, Path.Combine("docs", "index.md"), "# Menu\n\n![The menu](screenshots/menu.png)\n");

        file.IsPreviewMode = true;

        await UiText.SettleUntilAsync(window,
            () => Preview(window).GetVisualDescendants().OfType<ReplyImageView>().SingleOrDefault()?.ViewModel is { IsShown: true, Source.Path: var path } && path == png,
            "the image");
        window.Close();
    }
}
