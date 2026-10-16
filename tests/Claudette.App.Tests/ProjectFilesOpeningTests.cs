using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Settings;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Tests;

/// <summary>
/// Opening files in Claudette's editor (DESIGN.md §18, "Project files", "Opening files in the editor"): a file a reply
/// names opens in its project's Files tab, beside the conversation or in its place, at the line it names; a folder
/// shows in the explorer; what isn't text, or Settings → Editor, still sends it to the OS's app.
/// </summary>
public class ProjectFilesOpeningTests
{
    private static LinkClickedEventArgs Click(string address) =>
        new(MarkdownTextBlock.LinkClickEvent, new object(), new Uri(address, UriKind.RelativeOrAbsolute));

    private static string Write(string path, string text = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static readonly string FiftyLines = string.Concat(Enumerable.Range(1, 50).Select(i => $"line {i}\n"));

    [Fact]
    public async Task A_link_to_a_file_opens_it_in_the_Files_tab_beside_the_conversation_at_its_line()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var spec = Write(Path.Combine(h.WorkFolder, "docs", "weapons.md"), FiftyLines);

        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/weapons.md#L12-L14"));

        var files = h.Shell.Groups.Single().Files!;
        Assert.Equal([tab, (IShellTab)files], h.Shell.Panes);
        Assert.Same(files, h.Shell.Selected);
        var file = Assert.Single(files.OpenFiles);
        Assert.Equal(spec, file.Path);
        Assert.False(file.IsPreview);
        Assert.Equal((12, 1), (file.CaretLine, file.CaretColumn));
        Assert.Empty(h.Platform.OpenedFiles);
    }

    [Fact]
    public async Task With_Settings_so_the_Files_tab_takes_the_conversations_place()
    {
        await using var h = new TabTestHarness(s => s.Editor.FilesFromConversation = FilesFromConversation.InPlace);
        var tab = await h.OpenTabAsync();
        Write(Path.Combine(h.WorkFolder, "Player.cpp"), FiftyLines);

        await tab.OpenLinkCommand.ExecuteAsync(Click("Player.cpp:42:7"));

        var files = h.Shell.Groups.Single().Files!;
        Assert.Equal([(IShellTab)files], h.Shell.Panes);
        Assert.Same(files, h.Shell.Selected);
        Assert.False(tab.IsShown);
        Assert.Equal((42, 7), (files.Showing!.CaretLine, files.Showing.CaretColumn));
    }

    [Fact]
    public async Task A_Files_tab_already_on_screen_only_takes_the_focus()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var a = Write(Path.Combine(h.WorkFolder, "a.txt"));
        var b = Write(Path.Combine(h.WorkFolder, "b.txt"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("a.txt"));
        h.Shell.SelectedTab = tab;

        await tab.OpenLinkCommand.ExecuteAsync(Click("b.txt"));

        var files = h.Shell.Groups.Single().Files!;
        Assert.Equal([tab, (IShellTab)files], h.Shell.Panes);
        Assert.Equal([a, b], files.OpenFiles.Select(f => f.Path));
        Assert.Equal(b, files.Showing?.Path);
    }

    [Fact]
    public async Task A_file_thats_not_text_still_opens_in_the_oss_app_and_a_program_in_its_folder()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var image = Path.Combine(h.WorkFolder, "icon.png");
        await File.WriteAllBytesAsync(image, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0x0D], TestContext.Current.CancellationToken);
        Write(Path.Combine(h.WorkFolder, "bin", "game.exe"));

        await tab.OpenLinkCommand.ExecuteAsync(Click("icon.png"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("bin/game.exe"));

        Assert.Equal([image], h.Platform.OpenedFiles);
        Assert.Equal([Path.Combine(h.WorkFolder, "bin")], h.Platform.RevealedFolders);
        Assert.Null(h.Shell.Groups.Single().Files);
    }

    [Fact]
    public async Task A_script_opens_as_text_rather_than_running()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var script = Write(Path.Combine(h.WorkFolder, "tools", "build.bat"), "@echo off\r\n");

        await tab.OpenLinkCommand.ExecuteAsync(Click("tools/build.bat"));

        Assert.Equal(script, h.Shell.Groups.Single().Files!.Showing?.Path);
        Assert.Empty(h.Platform.OpenedFiles);
        Assert.Empty(h.Platform.RevealedFolders);
    }

    [Fact]
    public async Task A_folder_in_the_project_shows_in_the_explorer_opened_and_selected()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Write(Path.Combine(h.WorkFolder, "Source", "Player", "Player.cpp"));

        await tab.OpenLinkCommand.ExecuteAsync(Click("Source/Player/"));

        var files = h.Shell.Groups.Single().Files!;
        Assert.Equal("Source/Player", files.SelectedRow?.RelativePath);
        Assert.True(files.SelectedRow!.IsExpanded);
        Assert.Contains(files.Rows, r => r.Name == "Player.cpp");
        Assert.Empty(h.Platform.RevealedFolders);
    }

    [Fact]
    public async Task With_Settings_so_files_open_in_the_oss_app_as_before()
    {
        await using var h = new TabTestHarness(s => s.Editor.OpenFilesIn = OpenFilesIn.System);
        var tab = await h.OpenTabAsync();
        var spec = Write(Path.Combine(h.WorkFolder, "docs", "weapons.md"));

        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/weapons.md"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/"));

        Assert.Equal([spec], h.Platform.OpenedFiles);
        Assert.Equal([Path.Combine(h.WorkFolder, "docs")], h.Platform.RevealedFolders);
        Assert.Null(h.Shell.Groups.Single().Files);
    }

    [Fact]
    public async Task Go_to_file_opens_the_projects_Files_tab_with_its_box_up()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Shell.GoToFileCommand.Execute(null);

        var files = h.Shell.Groups.Single().Files!;
        Assert.True(files.IsFinding);
        Assert.Equal([tab, (IShellTab)files], h.Shell.Panes);
        Assert.Same(files, h.Shell.Selected);
    }

    [Fact]
    public async Task A_Files_tab_goes_into_a_pane_and_out_again_keeping_its_files()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var path = Write(Path.Combine(h.WorkFolder, "a.txt"));
        h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single());
        var files = h.Shell.Groups.Single().Files!;
        await files.OpenFileAsync(path, preview: false);
        h.Shell.SelectedTab = tab;

        h.Shell.OpenBesideCommand.Execute(files);

        Assert.Equal([tab, (IShellTab)files], h.Shell.Panes);
        Assert.True(files is { IsShown: true, IsSelected: true, IsInSplit: true, PaneIndex: 1 });

        files.ClosePaneCommand.Execute(null);

        Assert.Equal([tab], h.Shell.Panes);
        Assert.False(files.IsShown);
        Assert.Same(files, h.Shell.Groups.Single().Files);
        Assert.Equal(path, Assert.Single(files.OpenFiles).Path);
    }
}
