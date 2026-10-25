using Claudette.App.Editor;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.ProjectFiles;
using Claudette.Core.Protocol;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Tests;

/// <summary>
/// A Markdown file's preview in a Project Files tab (DESIGN.md §18, "Markdown preview"): its modes, what it shows and
/// when, its links and images, and Copy on its code blocks.
/// </summary>
public class MarkdownPreviewTests
{
    private const string Readme = "# Starfall\n\nA space shooter.\n\n## Saving\n\nIt saves.\n";

    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Write(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>The group's Files tab, opened from its button, with <paramref name="path"/> open in it.</summary>
    private static async Task<(ProjectFilesViewModel Files, ProjectFileViewModel File)> OpenAsync(TabTestHarness h, string path)
    {
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(group));
        var files = group.Files!;
        await TabTestHarness.Eventually(() => h.FolderWatcher.Folders.Contains(files.Folder), "the explorer");
        return (files, await files.OpenFileAsync(path, preview: false));
    }

    /// <summary>A click on a link in the preview, as LiveMarkdown's renderer passes it.</summary>
    private static LinkClickedEventArgs Click(string address) =>
        new(MarkdownTextBlock.LinkClickEvent, new object(), new Uri(address, UriKind.RelativeOrAbsolute));

    private static PlainEditorDocument Text(ProjectFileViewModel file) => (PlainEditorDocument)file.Document!;

    // ---- The modes -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_markdown_files_bar_shows_without_changes_and_offers_the_preview()
    {
        await using var h = new TabTestHarness();
        var notes = Write(Path.Combine(h.WorkFolder, "notes.markdown"), Readme);
        var (files, readme) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "README.md"), Readme));

        Assert.True(readme is { IsMarkdown: true, ShowsModeBar: true, ShowsDiffModes: false, IsEditMode: true, ShowsEditor: true, ShowsPreview: false });
        Assert.Equal("Markdown", readme.Language.Name);
        Assert.True((await files.OpenFileAsync(notes, preview: false)).IsMarkdown);
        var code = await files.OpenFileAsync(Write(Path.Combine(h.WorkFolder, "a.txt"), "a\n"), preview: false);
        Assert.True(code is { IsMarkdown: false, ShowsModeBar: false });
    }

    [Fact]
    public async Task Preview_shows_it_in_the_editors_place_and_Edit_and_preview_beside_it_one_choice_with_the_diff_modes()
    {
        await using var h = new TabTestHarness();
        var (_, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "README.md"), Readme));

        file.IsPreviewMode = true;
        Assert.True(file is { Mode: FileViewMode.Preview, ShowsEditor: false, ShowsPreview: true, IsDiffMode: false, Diff: null });

        file.IsEditAndPreviewMode = true;
        Assert.True(file is { Mode: FileViewMode.EditAndPreview, ShowsEditor: true, ShowsPreview: true, IsPreviewMode: false });

        file.IsSideBySideMode = true;
        Assert.True(file is { IsDiffMode: true, ShowsEditor: false, ShowsPreview: false, IsEditAndPreviewMode: false });
        // The diff modes show in the bar while they're showing.
        Assert.True(file.ShowsDiffModes);
    }

    [Fact]
    public async Task The_choice_is_kept_with_the_tab_and_a_file_that_isnt_markdown_opens_in_Edit()
    {
        await using var h = new TabTestHarness();
        var readme = Write(Path.Combine(h.WorkFolder, "README.md"), Readme);
        var code = Write(Path.Combine(h.WorkFolder, "a.txt"), "a\n");
        var saved = new ProjectFilesState
        {
            Folder = h.WorkFolder,
            Files = [new OpenFileState { Path = readme, Mode = FileViewMode.EditAndPreview }, new OpenFileState { Path = code, Mode = FileViewMode.Preview }],
            ShowingPath = readme,
        };
        h.Services.State.ProjectFiles = [saved];
        h.Services.State.SelectedTabId = saved.Id;

        InlineDispatcher.Run(() => h.Shell.Restore(null));
        var files = Assert.IsType<ProjectFilesViewModel>(h.Shell.Selected);
        await TabTestHarness.Eventually(() => files.OpenFiles.All(f => f.IsLoaded), "the files read");

        Assert.Equal([FileViewMode.EditAndPreview, FileViewMode.Edit], files.OpenFiles.Select(f => f.Mode));
        Assert.Equal(FileViewMode.EditAndPreview, files.CaptureState().Files[0].Mode);
        // Showing, it's parsed for the preview.
        await TabTestHarness.Eventually(() => files.OpenFiles[0].Markdown.ShownText == Readme, "the preview");
    }

    // ---- What it shows -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_editors_text_is_parsed_only_while_the_preview_shows_and_follows_typing_as_a_change()
    {
        await using var h = new TabTestHarness();
        var (_, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "README.md"), Readme));
        Assert.Null(file.Markdown.ShownText);

        file.IsPreviewMode = true;
        await TabTestHarness.Eventually(() => file.Markdown.ShownText == Readme, "the preview");

        // Typing shows 300 ms after it, as the change from what was shown.
        Text(file).Insert(2, "The ");
        Assert.Equal(Readme, file.Markdown.ShownText);
        h.Time.Advance(ProjectFileViewModel.MarkersDelay);
        var typed = Readme.Insert(2, "The ");
        await TabTestHarness.Eventually(() => file.Markdown.ShownText == typed, "the typing shown");
        Assert.Equal((2, 0, 4), (file.Markdown.LastChange!.Value.StartIndex, file.Markdown.LastChange.Value.Length, file.Markdown.LastChange.Value.NewLength));

        // While typing goes on, it catches up every 300 ms rather than waiting for a pause.
        Text(file).Insert(0, "a");
        h.Time.Advance(TimeSpan.FromMilliseconds(200));
        Text(file).Insert(0, "b");
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await TabTestHarness.Eventually(() => file.Markdown.ShownText == "ba" + typed, "caught up while typing");

        // In Edit it isn't parsed; back in the preview, it is again.
        file.IsEditMode = true;
        Text(file).Insert(0, "c");
        h.Time.Advance(ProjectFileViewModel.MarkersDelay);
        Assert.Equal("ba" + typed, file.Markdown.ShownText);
        file.IsEditAndPreviewMode = true;
        await TabTestHarness.Eventually(() => file.Markdown.ShownText == "cba" + typed, "the preview again");
    }

    [Fact]
    public void Front_matter_is_blanked_out_keeping_every_line_where_it_was()
    {
        Assert.Equal("   \n         \n   \n# Hi\n", MarkdownPreview.WithoutFrontMatter("---\ntitle: Hi\n---\n# Hi\n"));
        Assert.Equal("   \r\n      \r\n   \r\nText", MarkdownPreview.WithoutFrontMatter("---\r\na: 1 b\r\n...\r\nText"));
        // Unclosed, or not at the very top, it's left alone.
        Assert.Equal("---\ntitle: Hi\n", MarkdownPreview.WithoutFrontMatter("---\ntitle: Hi\n"));
        Assert.Equal("\n---\na: 1\n---\n", MarkdownPreview.WithoutFrontMatter("\n---\na: 1\n---\n"));
    }

    [Fact]
    public async Task A_file_over_1_MB_isnt_previewed_and_its_line_starts_are_kept_for_scrolling()
    {
        var preview = new MarkdownPreview();

        await preview.ShowAsync(new string('a', MarkdownPreview.MaxLength + 1));
        Assert.True(preview.IsTooLarge);
        Assert.Null(preview.ShownText);

        await preview.ShowAsync("# One\r\n\r\nTwo\nThree");
        Assert.False(preview.IsTooLarge);
        Assert.Equal([0, 7, 9, 13], preview.LineStarts);
        Assert.Equal((1, 1, 3, 4), (preview.LineAt(0), preview.LineAt(6), preview.LineAt(9), preview.LineAt(15)));
    }

    // ---- Links ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_link_to_a_file_is_read_against_the_files_own_folder_and_opens_it_in_the_editor_at_its_line()
    {
        await using var h = new TabTestHarness();
        var spec = Write(Path.Combine(h.WorkFolder, "docs", "specs", "weapons.md"), "# Weapons\n\nGuns.\n");
        var (files, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "docs", "index.md"), "[spec](specs/weapons.md#L3)\n"));

        await file.OpenLinkCommand.ExecuteAsync(Click("specs/weapons.md#L3"));

        Assert.Equal(spec, files.Showing?.Path);
        Assert.Equal(3, files.Showing?.CaretLine);
        Assert.Null(file.LinkProblem);
    }

    [Fact]
    public async Task A_link_to_a_file_that_isnt_there_says_so_until_the_next_link_or_mode()
    {
        await using var h = new TabTestHarness();
        var (files, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "docs", "index.md"), Readme));

        await file.OpenLinkCommand.ExecuteAsync(Click("specs/shots.md"));

        Assert.Equal("Couldn't find specs/shots.md.", file.LinkProblem);
        Assert.Same(file, files.Showing);
        file.IsPreviewMode = true;
        Assert.Null(file.LinkProblem);
    }

    [Fact]
    public async Task A_link_to_a_heading_scrolls_the_preview_and_a_web_link_opens_in_the_browser()
    {
        await using var h = new TabTestHarness();
        var (_, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "README.md"), Readme));
        var headings = new List<string>();
        file.HeadingRequested += headings.Add;

        await file.OpenLinkCommand.ExecuteAsync(Click("#saving"));
        await file.OpenLinkCommand.ExecuteAsync(Click("#claude%27s-changes"));
        await file.OpenLinkCommand.ExecuteAsync(Click("https://example.com/docs"));

        Assert.Equal(["saving", "claude's-changes"], headings);
        Assert.Equal(["https://example.com/docs"], h.Platform.OpenedUrls);
    }

    // ---- Images and Copy -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Its_images_are_read_against_its_own_folder_and_one_linked_opens_in_the_image_window()
    {
        await using var h = new TabTestHarness();
        var menu = Write(Path.Combine(h.WorkFolder, "docs", "screenshots", "menu.png"), TestImages.Png);
        var (_, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "docs", "index.md"), "![Menu](screenshots/menu.png)\n"));
        var opened = new List<ImageViewerViewModel>();
        h.Shell.ShowImageWindow = opened.Add;

        var image = file.ReplyImages.Classify("screenshots/menu.png");
        Assert.Equal((ReplyImageKind.InFolder, menu), (image.Kind, image.Path));
        Assert.Equal(ReplyImageKind.LocalFile, file.ReplyImages.Classify("../../elsewhere.png").Kind);

        await file.OpenLinkCommand.ExecuteAsync(Click("screenshots/menu.png"));

        var viewer = Assert.Single(opened);
        Assert.Equal(menu, viewer.Image.FilePath);
    }

    [Fact]
    public async Task Copy_on_a_code_block_copies_its_code_and_says_Copied_for_a_moment()
    {
        await using var h = new TabTestHarness();
        var (_, file) = await OpenAsync(h, Write(Path.Combine(h.WorkFolder, "README.md"), Readme));
        var copied = false;

        await file.CopyCodeAsync("dotnet build\n", new object(), shown => copied = shown);

        Assert.Equal("dotnet build", h.Platform.Clipboard);
        Assert.True(copied);
        h.Time.Advance(TabViewModel.CopiedFor);
        await TabTestHarness.Eventually(() => !copied, "Copied going back");
    }
}
