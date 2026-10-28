using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;

namespace Claudette.App.Tests;

/// <summary>Find in files and Replace in files (DESIGN.md §18, "Project files", "Finding in files").</summary>
public class FindInFilesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The popup opened as its shortcut does, with the search it starts done.</summary>
    private static async Task<FindInFilesViewModel> OpenAsync(TabTestHarness h, string query, bool replace = false)
    {
        InlineDispatcher.Run(() => (replace ? h.Shell.OpenReplaceInFilesCommand : h.Shell.OpenFindInFilesCommand).Execute(null));
        var find = h.Shell.FindInFiles!;
        InlineDispatcher.Run(() => find.Query = query);
        await SearchedAsync(h, find);
        return find;
    }

    /// <summary>Typing has stopped and the search it started has finished.</summary>
    private static async Task SearchedAsync(TabTestHarness h, FindInFilesViewModel find)
    {
        h.Time.Advance(FindInFilesViewModel.SearchDelay);
        await TabTestHarness.Eventually(() => !find.IsSearching && find.Summary.Length > 0 && !find.Summary.Contains("Searching", StringComparison.Ordinal), "the search");
    }

    [Fact]
    public async Task Typing_finds_the_matches_and_Enter_opens_one_selected_in_the_Files_tab()
    {
        await using var h = new TabTestHarness();
        Write(Path.Combine(h.WorkFolder, "Source", "Player.cpp"), "float Health = 100.f;\nfloat Speed = 600.f;\n");
        Write(Path.Combine(h.WorkFolder, "Config.h"), "#define SPEED 600\n");
        await h.OpenTabAsync();

        var find = await OpenAsync(h, "Speed");

        // Case is ignored unless asked: both files match.
        Assert.Equal("2 matches in 2 files", find.Summary);
        Assert.Equal(["Config.h 1", "Player.cpp 2"], find.Hits.Select(r => r.Location).Order());
        var player = find.Hits.Single(r => r.FileName == "Player.cpp");
        Assert.Equal(("float ", "Speed", " = 600.f;"), (player.Before, player.Match, player.After));

        InlineDispatcher.Run(() => find.MatchCase = true);
        await SearchedAsync(h, find);
        Assert.Equal("1 match in 1 file", find.Summary);

        InlineDispatcher.Run(() => find.SelectedHit = find.Hits.Single());
        await InlineDispatcher.RunAsync(() => find.OpenCommand.ExecuteAsync(null));

        // The popup goes, and the match is open in the project's Files tab, at its line.
        Assert.Null(h.Shell.FindInFiles);
        var files = h.Shell.Groups.Single().Files!;
        await TabTestHarness.Eventually(() => files.Showing?.Path == Path.Combine(h.WorkFolder, "Source", "Player.cpp"), "the file");
        Assert.Equal((2, 7), (files.Showing!.CaretLine, files.Showing.CaretColumn));
    }

    [Fact]
    public async Task Replace_all_asks_first_then_changes_open_files_unsaved_and_the_rest_on_disk()
    {
        await using var h = new TabTestHarness();
        var onDisk = Write(Path.Combine(h.WorkFolder, "a.txt"), "old one\r\nold two\r\n");
        var open = Write(Path.Combine(h.WorkFolder, "b.txt"), "old three\n");
        await h.OpenTabAsync();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single()));
        var files = h.Shell.Groups.Single().Files!;
        var file = await InlineDispatcher.RunAsync(() => files.OpenFileAsync(open, preview: false));
        await TabTestHarness.Eventually(() => file.IsLoaded, "the open file");

        var find = await OpenAsync(h, "old", replace: true);
        InlineDispatcher.Run(() => find.Replacement = "new");
        InlineDispatcher.Run(() => find.AskReplaceAllCommand.Execute(null));

        Assert.True(find.IsAskingToReplaceAll);
        Assert.Equal("Replace 3 matches in 2 files?", find.ReplaceAllText);
        Assert.Equal("old one\r\nold two\r\n", await File.ReadAllTextAsync(onDisk, Token));

        InlineDispatcher.Run(() => find.ConfirmReplaceAllCommand.Execute(null));
        await TabTestHarness.Eventually(() => find.ReplaceNotice is not null, "the replace");

        Assert.Equal("Replaced 3 matches in 2 files.", find.ReplaceNotice);
        // On disk, its line endings kept.
        Assert.Equal("new one\r\nnew two\r\n", await File.ReadAllTextAsync(onDisk, Token));
        // Open, in its editor and unsaved, so its Undo takes it back.
        Assert.Equal("new three\n", file.Document!.Text);
        Assert.True(file.IsModified);
        Assert.Equal("old three\n", await File.ReadAllTextAsync(open, Token));
    }

    [Fact]
    public async Task Find_in_folder_looks_only_in_that_folder()
    {
        await using var h = new TabTestHarness();
        Write(Path.Combine(h.WorkFolder, "Source", "Player.cpp"), "speed\n");
        Write(Path.Combine(h.WorkFolder, "Docs", "Notes.md"), "speed\n");
        await h.OpenTabAsync();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(h.Shell.Groups.Single()));
        var files = h.Shell.Groups.Single().Files!;
        await TabTestHarness.Eventually(() => files.Rows.Any(r => r.Name == "Source"), "the explorer");

        InlineDispatcher.Run(() => files.FindInFolderCommand.Execute(files.Rows.Single(r => r.Name == "Source")));
        var find = h.Shell.FindInFiles!;
        InlineDispatcher.Run(() => find.Query = "speed");
        await SearchedAsync(h, find);

        Assert.True(find.IsInDirectory);
        Assert.Equal(Path.Combine(h.WorkFolder, "Source"), find.Directory);
        Assert.Equal("Player.cpp", Assert.Single(find.Hits).FileName);
    }

    [Fact]
    public async Task The_popup_remembers_what_it_had_and_a_bad_regex_says_why()
    {
        await using var h = new TabTestHarness();
        Write(Path.Combine(h.WorkFolder, "a.txt"), "x\n");
        await h.OpenTabAsync();
        var find = await OpenAsync(h, "x");
        InlineDispatcher.Run(() =>
        {
            find.IsRegex = true;
            find.FileMask = "*.txt";
        });

        InlineDispatcher.Run(h.Shell.CloseFindInFiles);
        InlineDispatcher.Run(() => h.Shell.OpenFindInFilesCommand.Execute(null));
        var again = h.Shell.FindInFiles!;

        Assert.Equal(("x", true, "*.txt"), (again.Query, again.IsRegex, again.FileMask));
        Assert.Equal(("x", true), (h.Services.State.FindInFiles.Query, h.Services.State.FindInFiles.Regex));

        InlineDispatcher.Run(() => again.Query = "(unclosed");
        h.Time.Advance(FindInFilesViewModel.SearchDelay);
        await TabTestHarness.Eventually(() => again.HasProblem, "the problem");
        Assert.Empty(again.Hits);
    }

    [Fact]
    public async Task Kept_results_are_grouped_by_file_and_Replace_all_leaves_out_what_is_unticked()
    {
        await using var h = new TabTestHarness();
        var a = Write(Path.Combine(h.WorkFolder, "a.txt"), "old\nold\n");
        var b = Write(Path.Combine(h.WorkFolder, "b.txt"), "old\n");
        await h.OpenTabAsync();
        var find = await OpenAsync(h, "old", replace: true);
        // Until tool windows hold the Find window, the popup can't keep its results.
        Assert.False(find.CanKeep);

        var settings = find.Settings;
        var results = new FindResultsViewModel(new FindInFilesSearch(h.Services, h.Shell), settings, replaceMode: true, "new");
        await InlineDispatcher.RunAsync(() => results.SearchAgainCommand.ExecuteAsync(null));

        Assert.Equal(["a.txt", "b.txt"], results.Files.Select(f => f.FileName).Order());
        Assert.Equal(2, results.Files.Single(f => f.FileName == "a.txt").Hits.Count);
        Assert.Equal("old", results.Title);
        Assert.Equal("in the project", results.OptionsText);

        InlineDispatcher.Run(() =>
        {
            results.Files.Single(f => f.FileName == "a.txt").Hits[1].IsExcluded = true;
            results.AskReplaceAllCommand.Execute(null);
        });
        Assert.Equal("Replace 2 matches in 2 files? 1 left out.", results.ReplaceAllText);
        InlineDispatcher.Run(() => results.ConfirmReplaceAllCommand.Execute(null));
        await TabTestHarness.Eventually(() => results.ReplaceNotice is not null && !results.IsSearching, "the replace");

        Assert.Equal("new\nold\n", await File.ReadAllTextAsync(a, Token));
        Assert.Equal("new\n", await File.ReadAllTextAsync(b, Token));
        results.Dispose();
    }
}
