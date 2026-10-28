using Claudette.Core.Perforce;
using Claudette.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Perforce;

/// <summary>
/// Perforce for files (DESIGN.md §18, "Perforce in the Files tab"): finding a folder's workspace without asking a server
/// when nothing is set up, reading <c>p4 fstat</c>, the states' cache, and the commands' arguments and answers.
/// </summary>
public sealed class PerforceFilesTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-09-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeP4 _p4;
    private readonly PerforceClient _client;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"claudette-p4files-{Guid.NewGuid():N}");

    public PerforceFilesTests()
    {
        Directory.CreateDirectory(_folder);
        _p4 = new FakeP4(_time) { Root = _folder };
        _client = new PerforceClient(_p4, _time);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private PerforceTarget Target => new(_folder);

    // ---- Finding the workspace --------------------------------------------------------------------------------------

    [Fact]
    public async Task A_folder_with_nothing_set_up_isnt_asked_of_any_server()
    {
        _p4.Port = null;
        _p4.IsConfigured = false;

        var detection = await _client.FindWorkspaceAsync(Target, TestContext.Current.CancellationToken);

        Assert.Equal(PerforceDetectionOutcome.NotConfigured, detection.Outcome);
        Assert.Equal("set", Assert.Single(_p4.Runs).Command);
    }

    [Fact]
    public async Task A_folder_in_a_workspace_is_found_with_p4_info()
    {
        var detection = await _client.FindWorkspaceAsync(Target, TestContext.Current.CancellationToken);

        Assert.Equal(PerforceDetectionOutcome.Workspace, detection.Outcome);
        Assert.Equal("matt-ws", detection.Workspace!.Client);
        Assert.Equal(["set", "info"], _p4.Runs.Select(r => r.Command));
    }

    [Fact]
    public async Task An_unreachable_server_is_asked_again_only_after_a_while()
    {
        _p4.Unreachable = true;
        var finder = new PerforceWorkspaceFinder(_client, _time);

        Assert.Equal(PerforceDetectionOutcome.Unreachable, (await finder.DetectAsync(Target, TestContext.Current.CancellationToken)).Outcome);
        // Asked again at once: the answer is remembered.
        Assert.Equal(PerforceDetectionOutcome.Unreachable, (await finder.DetectAsync(Target, TestContext.Current.CancellationToken)).Outcome);
        Assert.Single(_p4.Runs, r => r.Command == "info");

        _p4.Unreachable = false;
        _time.Advance(PerforceWorkspaceFinder.RetryAfter);

        Assert.Equal(PerforceDetectionOutcome.Workspace, (await finder.DetectAsync(Target, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task A_folder_that_isnt_a_workspace_is_remembered_until_it_is_forgotten()
    {
        _p4.IsWorkspace = false;
        var finder = new PerforceWorkspaceFinder(_client, _time);

        Assert.Null(await finder.FindAsync(Target, TestContext.Current.CancellationToken));
        Assert.Null(await finder.FindAsync(Target with { Folder = _folder + Path.DirectorySeparatorChar }, TestContext.Current.CancellationToken));
        Assert.Single(_p4.Runs, r => r.Command == "info");

        _p4.IsWorkspace = true;
        finder.Forget(Target);

        Assert.NotNull(await finder.FindAsync(Target, TestContext.Current.CancellationToken));
    }

    // ---- p4 fstat ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Fstat_records_become_each_files_state()
    {
        var ztag = string.Join('\n',
            "... depotFile //depot/Game/Player.cpp", $"... clientFile {Path.Combine(_folder, "Player.cpp")}", "... headAction edit", "... haveRev 4",
            "... action edit", "... change 12345", "",
            "... depotFile //depot/Game/New%40x2.png", $"... clientFile {Path.Combine(_folder, "New%40x2.png")}", "... action add", "... change default", "",
            "... depotFile //depot/Game/Old.cpp", $"... clientFile {Path.Combine(_folder, "Old.cpp")}", "... headAction edit", "... haveRev 2",
            "... otherOpen0 anna@anna-ws", "... otherOpen1 bo@bo-ws", "",
            "... depotFile //depot/Game/Gone.cpp", $"... clientFile {Path.Combine(_folder, "Gone.cpp")}", "... headAction delete", "",
            "... depotFile //depot/Game/Odd.cpp", $"... clientFile {Path.Combine(_folder, "Odd.cpp")}", "... headAction edit", "... haveRev 1", "... action integrate",
            "... change 7", "... somethingNew yes", "");

        var states = PerforceFileState.ParseFstat(ztag);

        Assert.Equal(
            [PerforceFileStatus.CheckedOut, PerforceFileStatus.MarkedForAdd, PerforceFileStatus.InDepot, PerforceFileStatus.NotInDepot, PerforceFileStatus.Opened],
            states.Select(s => s.Status));
        Assert.Equal("Checked out in CL 12345.", states[0].Describe());
        // Perforce's escapes are undone in the local path.
        Assert.Equal(Path.Combine(_folder, "New@x2.png"), states[1].Path);
        Assert.Equal("Marked for add in the default changelist.", states[1].Describe());
        Assert.Equal("Opened by anna@anna-ws and bo@bo-ws.", states[2].Describe());
        Assert.Null(states[3].Describe());
        Assert.Equal("Opened for integrate in CL 7.", states[4].Describe());
        Assert.True(states[0].HasDepotRevision);
        Assert.False(states[1].HasDepotRevision);
    }

    // ---- The states' cache ------------------------------------------------------------------------------------------

    [Fact]
    public void A_folders_answer_replaces_its_states_and_files_it_doesnt_name_arent_in_the_depot()
    {
        var cache = new PerforceStatusCache();
        var file = Path.Combine(_folder, "a.cpp");

        Assert.Null(cache.Find(file));

        var ask = cache.Begin(_folder);
        Assert.True(cache.Complete(_folder, ask, [new PerforceFileState(file, PerforceFileStatus.CheckedOut, Changelist: "default", Action: "edit", HaveRevision: 1)]));

        Assert.Equal(PerforceFileStatus.CheckedOut, cache.Find(file)!.Status);
        Assert.Equal(PerforceFileStatus.NotInDepot, cache.Find(Path.Combine(_folder, "b.cpp"))!.Status);
        Assert.True(cache.Knows(_folder));
    }

    [Fact]
    public void An_ask_a_later_one_overtook_drops_its_answer()
    {
        var cache = new PerforceStatusCache();
        var file = Path.Combine(_folder, "a.cpp");
        var first = cache.Begin(_folder);
        var second = cache.Begin(_folder);

        Assert.True(cache.Complete(_folder, second, [new PerforceFileState(file, PerforceFileStatus.CheckedOut)]));
        Assert.False(cache.Complete(_folder, first, [new PerforceFileState(file, PerforceFileStatus.InDepot)]));

        Assert.Equal(PerforceFileStatus.CheckedOut, cache.Find(file)!.Status);
    }

    // ---- The commands -----------------------------------------------------------------------------------------------

    [Fact]
    public void Commands_name_their_changelist_and_escape_Perforces_wildcards()
    {
        var file = Path.Combine(_folder, "icon@2x.png");
        var spec = PerforceFileCommands.FileSpec(file);

        Assert.EndsWith("icon%402x.png", spec, StringComparison.Ordinal);
        Assert.Equal(["edit", "-c", "12345", spec], PerforceFileCommands.Edit([spec], 12345));
        Assert.Equal(["edit", spec], PerforceFileCommands.Edit([spec], null));
        // A name with a wildcard character is added as it is, with -f.
        Assert.Equal(["add", "-f", file], PerforceFileCommands.Add([file], null));
        Assert.Equal(["delete", "-c", "7", "-k", spec], PerforceFileCommands.Delete([spec], 7, keepLocal: true));
        Assert.Equal(["move", "-k", "from", "to"], PerforceFileCommands.Move("from", "to", null));
        Assert.EndsWith($"{Path.DirectorySeparatorChar}...", PerforceFileCommands.FolderSpec(_folder), StringComparison.Ordinal);
        Assert.Equal(["-ztag", "fstat", "-T", PerforceFileState.Fields, spec], PerforceFileCommands.Fstat([spec]));
    }

    [Fact]
    public void Perforces_answers_that_mean_nothing_to_do_arent_problems()
    {
        PerforceCommandResult Result(string output, string error) => new(true, error.Length > 0 ? 1 : 0, output, error);

        Assert.Null(PerforceFileCommands.Problem(Result("//depot/a.cpp#1 - currently opened for edit", "")));
        Assert.Null(PerforceFileCommands.Problem(Result("a.cpp - can't add existing file", "")));
        // Claudette's own commands don't complain about files Perforce doesn't have; the user's do.
        Assert.Null(PerforceFileCommands.Problem(Result("", "a.cpp - no such file(s)."), PerforceQuietness.Automatic));
        Assert.Equal("a.cpp - no such file(s).", PerforceFileCommands.Problem(Result("", "a.cpp - no such file(s).")));
        Assert.Equal("//depot/a.cpp - locked by anna@anna-ws", PerforceFileCommands.Problem(Result("", "//depot/a.cpp - locked by anna@anna-ws")));
        Assert.Equal("Perforce didn't answer in time.", PerforceFileCommands.Problem(PerforceCommandResult.NotRun(timedOut: true)));
    }

    [Fact]
    public async Task Commands_run_in_the_folder_with_its_server_and_user()
    {
        var file = Path.Combine(_folder, "a.cpp");
        _p4.Has(file);

        var result = await _client.RunCommandAsync(Target with { Port = "ssl:other:1666", User = "anna" }, PerforceFileCommands.Edit([PerforceFileCommands.FileSpec(file)], 42), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Ran);
        Assert.Null(PerforceFileCommands.Problem(result));
        var run = Assert.Single(_p4.FileCommands);
        Assert.Equal(("ssl:other:1666", "anna", _folder), (run.Port, run.User, run.Spec.WorkingDirectory));
        Assert.Equal(("edit", "42"), (_p4.Find(file)!.Action, _p4.Find(file)!.Change));
    }
}
