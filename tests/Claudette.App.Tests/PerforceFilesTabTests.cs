using System.Text.Json.Nodes;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Settings;
using Claudette.Testing;

namespace Claudette.App.Tests;

/// <summary>
/// Perforce in the Files tab, and opening Claude's and the user's changes in Perforce (DESIGN.md §18, "Perforce in the
/// Files tab", "Opening files in Perforce"), against a pretend <c>p4</c>.
/// </summary>
public class PerforceFilesTabTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A harness whose processes are the fake p4, the work folder its workspace, and ticket handling off.</summary>
    private static (TabTestHarness H, FakeP4 P4) Harness(Action<PerforceSettings>? configure = null)
    {
        var p4 = new FakeP4();
        var h = new TabTestHarness(settings => configure?.Invoke(settings.Perforce), launcher: p4);
        p4.Time = h.Time;
        p4.Root = h.WorkFolder;
        return (h, p4);
    }

    private static string Write(string path, string text = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static async Task<(TabViewModel Tab, ProjectFilesViewModel Files)> OpenFilesAsync(TabTestHarness h)
    {
        var tab = await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(group));
        var files = group.Files!;
        await TabTestHarness.Eventually(() => h.FolderWatcher.Folders.Contains(files.Folder), "the explorer");
        await TabTestHarness.Eventually(() => files.IsPerforceWorkspace, "the workspace");
        return (tab, files);
    }

    private static ExplorerRow Row(ProjectFilesViewModel files, string name) => InlineDispatcher.Read(() => files.Rows.Single(r => r.Name == name));

    // ---- The state in the tree, and the menu --------------------------------------------------------------------------

    [Fact]
    public async Task The_explorer_marks_what_Perforce_has_open_and_each_rows_menu_follows()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var editing = Write(Path.Combine(h.WorkFolder, "Player.cpp"));
        var synced = Write(Path.Combine(h.WorkFolder, "Config.h"));
        Write(Path.Combine(h.WorkFolder, "Notes.txt"));
        p4.Has(editing).Action = "edit";
        p4.Find(editing)!.Change = "12345";
        p4.Has(synced).OtherOpens.Add("anna@anna-ws");

        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceMark == "edit" && Row(files, "Config.h").PerforceMark == "others", "the marks");

        var player = Row(files, "Player.cpp");
        Assert.Equal("Checked out in CL 12345.", player.PerforceTip);
        Assert.Equal((false, false, false, true, true), (player.CanCheckOut, player.CanMarkForAdd, player.CanMarkForDelete, player.CanRevert, player.CanDiffAgainstDepot));
        var config = Row(files, "Config.h");
        Assert.Equal(("others", "Opened by anna@anna-ws."), (config.PerforceMark, config.PerforceTip));
        Assert.Equal((true, false, true, false), (config.CanCheckOut, config.CanMarkForAdd, config.CanMarkForDelete, config.CanRevert));
        // A file Perforce doesn't have gets no mark, which would be noise, but it can be added.
        var notes = Row(files, "Notes.txt");
        Assert.Null(notes.PerforceMark);
        Assert.Equal((false, true), (notes.CanCheckOut, notes.CanMarkForAdd));
        // A folder's states are asked in one fstat, of the files in it (the folder's read and the workspace's being found
        // can each ask, once).
        Assert.All(p4.Runs.Where(r => r.Command == "fstat"), fstat => Assert.Equal(Path.Combine(h.WorkFolder, "*"), fstat.Arguments[^1]));
    }

    [Fact]
    public async Task Turning_off_the_state_in_the_tree_takes_the_marks_away_and_asks_no_more()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var editing = Write(Path.Combine(h.WorkFolder, "Player.cpp"));
        p4.Has(editing).Action = "edit";
        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceMark == "edit", "the mark");

        h.Services.Settings.Perforce.ShowFileState = false;
        InlineDispatcher.Run(h.Services.SaveSettings);

        Assert.Null(Row(files, "Player.cpp").PerforceMark);
        // The menu still knows the file is Perforce's.
        Assert.True(Row(files, "Player.cpp").InPerforce);
    }

    [Fact]
    public async Task Check_out_and_revert_from_the_menu_run_p4_and_the_mark_follows()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "x");
        p4.Has(path, "x");
        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").CanCheckOut && Row(files, "Player.cpp").PerforceState is not null, "the state");

        await InlineDispatcher.RunAsync(() => files.CheckOutRowCommand.ExecuteAsync(Row(files, "Player.cpp")));

        var edit = Assert.Single(p4.FileCommands);
        Assert.Equal("edit", edit.Command);
        // No session tab works in a changelist: the default one.
        Assert.DoesNotContain("-c", edit.Arguments);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceMark == "edit", "the mark");
        Assert.Null(files.PerforceProblem);

        // Nothing changed since: Revert doesn't ask.
        await InlineDispatcher.RunAsync(() => files.RevertRowCommand.ExecuteAsync(Row(files, "Player.cpp")));

        Assert.Null(files.PerforceAsking);
        Assert.Equal(["edit", "revert"], p4.FileCommands.Select(r => r.Command));
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceMark is null, "the mark gone");
    }

    [Fact]
    public async Task Revert_asks_first_when_the_file_has_changes()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "changed");
        var depot = p4.Has(path, "original");
        depot.Action = "edit";
        depot.Differs = true;
        var (_, files) = await OpenFilesAsync(h);

        await InlineDispatcher.RunAsync(() => files.RevertRowCommand.ExecuteAsync(Row(files, "Player.cpp")));

        Assert.Equal(("Revert Player.cpp? Its changes are lost.", "Revert"), (files.PerforceAsking?.Text, files.PerforceAsking?.ConfirmText));
        Assert.Empty(p4.FileCommands);

        await InlineDispatcher.RunAsync(() => files.ConfirmPerforceCommand.ExecuteAsync(null));

        Assert.Null(files.PerforceAsking);
        Assert.Equal("revert", Assert.Single(p4.FileCommands).Command);
    }

    [Fact]
    public async Task What_p4_refuses_is_said_under_the_explorers_header()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"));
        p4.Has(path).Refuse = "//depot/Player.cpp - locked by anna@anna-ws";
        var (_, files) = await OpenFilesAsync(h);

        await InlineDispatcher.RunAsync(() => files.CheckOutRowCommand.ExecuteAsync(Row(files, "Player.cpp")));

        Assert.Equal("Couldn't check out Player.cpp: //depot/Player.cpp - locked by anna@anna-ws", files.PerforceProblem);
        InlineDispatcher.Run(() => files.DismissPerforceProblemCommand.Execute(null));
        Assert.Null(files.PerforceProblem);
    }

    [Fact]
    public async Task Diff_against_the_depot_shows_the_revision_synced_here()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "mine\n");
        p4.Has(path, "theirs\n").Action = "edit";
        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceMark == "edit", "the state");
        Diffs.DiffSource? shown = null;
        files.DiffRequested += source => shown = source;

        await InlineDispatcher.RunAsync(() => files.DiffRowAgainstDepotCommand.ExecuteAsync(Row(files, "Player.cpp")));

        Assert.NotNull(shown);
        Assert.Equal((path, "theirs\n"), (shown.Path, shown.Before));
        // The revision synced here, written to a file as it is.
        var print = p4.Runs.Single(r => r.Command == "print");
        Assert.Equal(["-q", "-o"], print.Arguments.Take(2));
        Assert.EndsWith("#have", print.Arguments[^1], StringComparison.Ordinal);
    }

    // ---- Opening files in Perforce, from the Files tab ----------------------------------------------------------------

    [Fact]
    public async Task A_file_Perforce_hasnt_checked_out_opens_editable_and_its_first_edit_checks_it_out()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "x");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        p4.Has(path, "x");
        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceState is not null, "the state");

        var file = await InlineDispatcher.RunAsync(() => files.OpenFileAsync(path, preview: false));
        await TabTestHarness.Eventually(() => file!.IsLoaded && !file.IsReadOnly, "the file, editable");

        InlineDispatcher.Run(() => ((PlainEditorDocument)file!.Document!).Insert(0, "y"));
        await TabTestHarness.Eventually(() => p4.FileCommands.Any(r => r.Command == "edit"), "the check-out");
        var saved = await InlineDispatcher.RunAsync(() => file!.SaveAsync());

        Assert.True(saved);
        Assert.Equal("yx", await File.ReadAllTextAsync(path, Token));
        Assert.Equal("edit", p4.Find(path)!.Action);
        Assert.Null(files.PerforceProblem);
    }

    [Fact]
    public async Task With_checking_out_off_a_file_Perforce_hasnt_checked_out_stays_read_only_and_says_so()
    {
        var (h, p4) = Harness(p => p.CheckOutBeforeChanges = false);
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "x");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        p4.Has(path, "x");
        var (_, files) = await OpenFilesAsync(h);
        await TabTestHarness.Eventually(() => Row(files, "Player.cpp").PerforceState is not null, "the state");

        var file = await InlineDispatcher.RunAsync(() => files.OpenFileAsync(path, preview: false));
        await TabTestHarness.Eventually(() => file!.ReadOnlyReason?.Contains("isn't checked out", StringComparison.Ordinal) == true, "the reason");

        // Checking it out from the tab's menu makes it editable.
        await InlineDispatcher.RunAsync(() => files.CheckOutFileCommand.ExecuteAsync(file));
        await TabTestHarness.Eventually(() => !file!.IsReadOnly, "the file, editable");
        File.SetAttributes(path, FileAttributes.Normal);
    }

    [Fact]
    public async Task New_files_are_marked_for_add_and_renames_and_deletes_follow_in_Perforce()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Brain.cpp"), "think");
        p4.Has(path, "think");
        var (_, files) = await OpenFilesAsync(h);

        // New file.
        await InlineDispatcher.RunAsync(() => files.NewFileCommand.ExecuteAsync(null));
        files.NamingRow!.NameText = "Weapon.cpp";
        await InlineDispatcher.RunAsync(() => files.CommitNamingCommand.ExecuteAsync(null));
        var weapon = Path.Combine(h.WorkFolder, "Weapon.cpp");
        await TabTestHarness.Eventually(() => p4.Find(weapon)?.Action == "add", "the add");

        // Rename: checked out, then moved on the server, the files on disk left alone.
        InlineDispatcher.Run(() => files.RenameRowCommand.Execute(Row(files, "Brain.cpp")));
        files.NamingRow!.NameText = "Mind.cpp";
        await InlineDispatcher.RunAsync(() => files.CommitNamingCommand.ExecuteAsync(null));
        var mind = Path.Combine(h.WorkFolder, "Mind.cpp");
        await TabTestHarness.Eventually(() => p4.Find(mind)?.Action == "move/add", "the move");
        var move = p4.FileCommands.Single(r => r.Command == "move");
        Assert.Equal("-k", move.Arguments[0]);
        Assert.Equal("move/delete", p4.Find(path)!.Action);

        // Delete: to the trash, then marked for delete; a file opened for add is reverted instead.
        InlineDispatcher.Run(() => files.DeleteRowCommand.Execute(Row(files, "Weapon.cpp")));
        await TabTestHarness.Eventually(() => files.IsAskingToDelete, "the question");
        await InlineDispatcher.RunAsync(() => files.ConfirmDeleteCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => p4.Find(weapon) is null, "the add reverted");
        Assert.DoesNotContain(p4.FileCommands, r => r.Command == "delete");
    }

    [Fact]
    public async Task A_folder_with_nothing_set_up_for_Perforce_runs_nothing_but_p4_set()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        p4.Port = null;
        p4.IsConfigured = false;
        Write(Path.Combine(h.WorkFolder, "Player.cpp"));
        var tab = await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(group));
        var files = group.Files!;
        await TabTestHarness.Eventually(() => h.FolderWatcher.Folders.Contains(files.Folder), "the explorer");
        await TabTestHarness.Eventually(() => p4.Runs.Count(r => r.Command == "set") >= 1, "the look");
        // Nothing marks the moment a server would have been asked: the look is done once it has said there's no workspace.
        await Waiting.NeverAsync(() => p4.Runs.Any(r => r.Command != "set"), "a command for the server");

        Assert.False(files.IsPerforceWorkspace);
        Assert.False(Row(files, "Player.cpp").InPerforce);
        Assert.NotNull(tab);
    }

    // ---- Opening Claude's changes in Perforce -------------------------------------------------------------------------

    /// <summary>Claude Code calling the check-out hook before a file tool, as Claude Code calls a registered hook.</summary>
    private static string FileToolHook(TabTestHarness h, string requestId, string tool, string path)
    {
        var initialize = h.Transport.Sent.First(m => m["request"]?["subtype"]?.GetValue<string>() == "initialize");
        // The last of the file tools' hooks is Perforce's; the one before it is Changed files' (DESIGN.md §8).
        var callback = initialize["request"]!["hooks"]!["PreToolUse"]!.AsArray()
            .Last(m => m!["matcher"]!.GetValue<string>() == ChangedFilesViewModel.FileToolMatcher)!["hookCallbackIds"]![0]!.GetValue<string>();
        return new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = requestId,
            ["request"] = new JsonObject
            {
                ["subtype"] = "hook_callback",
                ["callback_id"] = callback,
                ["input"] = new JsonObject
                {
                    ["hook_event_name"] = "PreToolUse",
                    ["tool_name"] = tool,
                    ["tool_input"] = new JsonObject { ["file_path"] = path, ["old_string"] = "a", ["new_string"] = "b" },
                    ["tool_use_id"] = "toolu_" + requestId,
                },
                ["tool_use_id"] = "toolu_" + requestId,
            },
        }.ToJsonString();
    }

    private static async Task<JsonObject> WaitForAnswerAsync(TabTestHarness h, string requestId)
    {
        JsonObject? answer = null;
        await TabTestHarness.Eventually(() => (answer = h.Transport.Sent.LastOrDefault(m =>
            m["type"]?.GetValue<string>() == "control_response" && m["response"]?["request_id"]?.GetValue<string>() == requestId)) is not null, "the hook's answer");
        Assert.Equal("success", answer!["response"]!["subtype"]!.GetValue<string>());
        return answer["response"]!["response"]!.AsObject();
    }

    [Fact]
    public async Task Claudes_Edit_checks_the_file_out_first_and_the_hook_never_changes_the_call()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "a");
        p4.Has(path, "a");
        await h.OpenTabAsync();

        h.Transport.Emit(FileToolHook(h, "hc1", "Edit", path));
        var answer = await WaitForAnswerAsync(h, "hc1");

        Assert.Equal("""{"continue":true}""", answer.ToJsonString());
        Assert.Equal("edit", Assert.Single(p4.FileCommands).Command);
        Assert.Equal("edit", p4.Find(path)!.Action);
    }

    [Fact]
    public async Task The_hook_answers_in_time_when_Perforce_doesnt()
    {
        var (h, p4) = Harness();
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "a");
        p4.Has(path, "a");
        var tab = await h.OpenTabAsync();
        // Found first, so only the check-out waits.
        await h.Services.Perforce.Files.WorkspaceForAsync(h.WorkFolder, Token);
        p4.HoldFileCommands = new TaskCompletionSource();

        h.Transport.Emit(FileToolHook(h, "hc1", "Write", path));
        await TabTestHarness.Eventually(() => p4.FileCommands.Any(), "the check-out started");
        h.Time.Advance(Services.PerforceFilesService.HookTimeout - Services.PerforceFilesService.HookMargin);
        var answer = await WaitForAnswerAsync(h, "hc1");

        Assert.Equal("""{"continue":true}""", answer.ToJsonString());
        await TabTestHarness.Eventually(() => tab.Items.OfType<Conversation.NoteItem>().Any(n => n.Text.Contains("didn't answer in time", StringComparison.Ordinal)), "the note");
        p4.HoldFileCommands.TrySetResult();
    }

    [Fact]
    public async Task With_checking_out_off_the_hook_asks_nothing_of_Perforce()
    {
        var (h, p4) = Harness(p => p.CheckOutBeforeChanges = false);
        await using var _h = h;
        var path = Write(Path.Combine(h.WorkFolder, "Player.cpp"), "a");
        p4.Has(path, "a");
        await h.OpenTabAsync();

        h.Transport.Emit(FileToolHook(h, "hc1", "Edit", path));
        var answer = await WaitForAnswerAsync(h, "hc1");

        Assert.Equal("""{"continue":true}""", answer.ToJsonString());
        Assert.Empty(p4.FileCommands);
    }
}
