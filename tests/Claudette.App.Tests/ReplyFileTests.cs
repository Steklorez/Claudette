using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Protocol;
using Claudette.Core.Settings;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Tests;

/// <summary>
/// Files named in replies (DESIGN.md §18, "Files named in Claude's replies"): a click on a link to one, or on inline code
/// that names one, opens it in the OS's app (with Settings → Editor so set; in the editor by default, ProjectFilesOpeningTests),
/// shows a program in its folder instead, offers a menu when a name matches several files, and says so when there's none.
/// </summary>
public class ReplyFileTests
{
    /// <summary>Settings → Editor → Open files in the app the OS uses, as before the editor (DESIGN.md §18, "Project files").</summary>
    private static void OpenInSystem(AppSettings settings) => settings.Editor.OpenFilesIn = OpenFilesIn.System;

    private const string Result = """{"type":"result","subtype":"success","is_error":false,"session_id":"s1","duration_ms":1200}""";

    /// <summary>A click on a link in a reply's Markdown, as LiveMarkdown's renderer passes it.</summary>
    private static LinkClickedEventArgs Click(string address) =>
        new(MarkdownTextBlock.LinkClickEvent, new object(), new Uri(address, UriKind.RelativeOrAbsolute));

    /// <summary>A click on inline code that became a link (<c>Views/ReplyFileCode</c>).</summary>
    private static LinkClickedEventArgs ClickCode(string code) =>
        new(MarkdownTextBlock.LinkClickEvent, new object(), new Uri(Uri.EscapeDataString(code), UriKind.Relative));

    private static string Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    /// <summary>A turn in which Claude wrote <paramref name="path"/>.</summary>
    private static async Task WriteTurnAsync(TabTestHarness h, TabViewModel tab, string id, string path)
    {
        // The turn before it left a footer last, so it's this turn's that's waited for.
        var footers = InlineDispatcher.Read(() => tab.Items.OfType<TurnSummaryItem>().Count());
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = "Write", ["input"] = new JsonObject { ["file_path"] = path, ["content"] = "# Weapons" } }) },
        });
        Write(path);
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = "File created." }) },
        });
        h.Transport.Emit(Result);
        await TabTestHarness.Eventually(() => tab.Status != TabStatus.Working && tab.Items.OfType<TurnSummaryItem>().Count() > footers, "the turn's footer");
    }

    [Fact]
    public async Task A_link_to_a_file_in_the_tabs_folder_opens_it_in_the_oss_app()
    {
        await using var h = new TabTestHarness(OpenInSystem);
        var tab = await h.OpenTabAsync();
        var spec = Write(Path.Combine(h.WorkFolder, "docs", "specs", "weapons.md"));

        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/specs/weapons.md#L12"));
        await tab.OpenLinkCommand.ExecuteAsync(Click(new Uri(spec).AbsoluteUri));

        Assert.Equal([spec, spec], h.Platform.OpenedFiles);
        Assert.Empty(h.Platform.OpenedUrls);
    }

    [Fact]
    public async Task Web_links_still_open_in_the_browser_and_other_schemes_do_nothing()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        await tab.OpenLinkCommand.ExecuteAsync(Click("https://example.com/docs"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("javascript:alert(1)"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("vscode://file/C:/work/weapons.md"));

        Assert.Equal(["https://example.com/docs"], h.Platform.OpenedUrls);
        Assert.Empty(h.Platform.OpenedFiles);
        Assert.DoesNotContain(tab.Items, i => i is NoteItem);
    }

    [Fact]
    public async Task A_program_or_script_shows_in_its_folder_instead_of_running_and_a_folder_opens_as_one()
    {
        await using var h = new TabTestHarness(OpenInSystem);
        var tab = await h.OpenTabAsync();
        Write(Path.Combine(h.WorkFolder, "docs", "check.bat"));
        Write(Path.Combine(h.WorkFolder, "tools", "build.py"));
        var docs = Path.Combine(h.WorkFolder, "docs");

        // Named as something harmless, as a prompt-injected reply would.
        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/check.bat"));
        await tab.OpenLinkCommand.ExecuteAsync(ClickCode("tools/build.py"));
        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/"));

        Assert.Empty(h.Platform.OpenedFiles);
        Assert.Equal([docs, Path.Combine(h.WorkFolder, "tools"), docs], h.Platform.RevealedFolders);
    }

    [Fact]
    public async Task A_name_is_the_file_the_session_worked_on_and_opens_from_its_inline_code()
    {
        await using var h = new TabTestHarness(OpenInSystem);
        var tab = await h.OpenTabAsync();
        var spec = Path.Combine(h.WorkFolder, "docs", "specs", "weapons.md");
        // Another file with the name, which the session didn't touch.
        Write(Path.Combine(h.WorkFolder, "Plugins", "Combat", "weapons.md"));
        h.Transport.EmitTurn("Writing the spec.");
        // The tab is idle before the turn arrives too: its footer is what says it has.
        await TabTestHarness.Eventually(() => tab.Items.LastOrDefault() is TurnSummaryItem, "the first turn");
        await WriteTurnAsync(h, tab, "w1", spec);

        var found = await tab.ReplyFiles.FindAsync(ReplyFileReference.FromCode("weapons.md")!, clicked: false, TestContext.Current.CancellationToken);
        await tab.OpenLinkCommand.ExecuteAsync(ClickCode("weapons.md:42"));

        Assert.Equal([spec], found);
        // Remembered, so a reply built again links it at once.
        Assert.Equal([spec], tab.ReplyFiles.Found("weapons.md"));
        Assert.Equal([spec], h.Platform.OpenedFiles);
        Assert.Empty(h.Platform.Menus);
    }

    [Fact]
    public async Task A_name_that_matches_several_project_files_offers_them_in_a_menu()
    {
        await using var h = new TabTestHarness(OpenInSystem);
        var tab = await h.OpenTabAsync();
        Write(Path.Combine(h.WorkFolder, "docs", "weapons.md"));
        var plugin = Write(Path.Combine(h.WorkFolder, "Plugins", "Combat", "weapons.md"));

        h.Platform.ChoiceToPick = 1;
        await tab.OpenLinkCommand.ExecuteAsync(ClickCode("weapons.md"));
        h.Platform.ChoiceToPick = null;
        await tab.OpenLinkCommand.ExecuteAsync(ClickCode("weapons.md"));

        // Shallowest first, from the tab's folder.
        Assert.Equal(2, h.Platform.Menus.Count);
        Assert.All(h.Platform.Menus, menu => Assert.Equal([Path.Combine("docs", "weapons.md"), Path.Combine("Plugins", "Combat", "weapons.md")], menu));
        // The pick opens; closing the menu without one opens nothing.
        Assert.Equal([plugin], h.Platform.OpenedFiles);
    }

    [Fact]
    public async Task A_link_to_a_file_thats_not_there_says_so()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        await tab.OpenLinkCommand.ExecuteAsync(Click("docs/specs/shots.md"));

        var note = Assert.IsType<NoteItem>(tab.Items.Last());
        Assert.Equal("Couldn't find docs/specs/shots.md.", note.Text);
        Assert.Equal(NoteKind.Warning, note.Kind);
        Assert.Empty(h.Platform.OpenedFiles);
    }
}
