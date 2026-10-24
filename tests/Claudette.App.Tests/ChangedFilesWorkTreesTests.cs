using System.Text.Json.Nodes;
using Claudette.App.Tests.Support;

namespace Claudette.App.Tests;

/// <summary>
/// Claude moving between worktrees, and the working trees Changed files groups its rows by (DESIGN.md §4, "Worktree
/// tabs"; §8, "Working trees"), in repositories written by hand (<see cref="FakeGitFolders"/>).
/// </summary>
public class ChangedFilesWorkTreesTests
{
    [Fact]
    public async Task The_tab_follows_Claude_into_a_worktree_and_back_out_staying_in_its_group()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        var tab = await h.OpenTabAsync();

        Call(h, "w1", "EnterWorktree", new JsonObject { ["name"] = "brisk-otter" },
            new JsonObject { ["worktreePath"] = worktree, ["worktreeBranch"] = "worktree-brisk-otter", ["message"] = "Created worktree" });

        await TabTestHarness.Eventually(() => tab.IsInWorktree, "the tab in the worktree");
        Assert.Equal(worktree, tab.Folder);
        Assert.Equal(h.WorkFolder, tab.GroupFolder);
        Assert.Equal("brisk-otter", tab.WorktreeName);
        Assert.Contains(tab.Items.OfType<Conversation.NoteItem>(), n => n.Text.StartsWith("Working in the worktree brisk-otter", StringComparison.Ordinal));

        Call(h, "w2", "ExitWorktree", new JsonObject { ["action"] = "keep" },
            new JsonObject { ["action"] = "keep", ["originalCwd"] = h.WorkFolder, ["worktreePath"] = worktree, ["restoredCwd"] = h.WorkFolder, ["message"] = "Exited worktree" });

        await TabTestHarness.Eventually(() => !tab.IsInWorktree, "the tab back in its folder");
        Assert.Equal(h.WorkFolder, tab.Folder);
        Assert.Contains(tab.Items.OfType<Conversation.NoteItem>(), n => n.Text.StartsWith("Back in ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_session_resumed_in_a_worktree_follows_its_init_but_not_a_cd_into_a_subfolder()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "calm-heron");
        var src = Directory.CreateDirectory(Path.Combine(h.WorkFolder, "src")).FullName;
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Init(src));
        h.Transport.Emit(Init(worktree));

        await TabTestHarness.Eventually(() => tab.IsInWorktree, "the tab in the worktree");
        Assert.Equal(worktree, tab.Folder);
    }

    [Fact]
    public async Task Changes_in_other_working_trees_are_grouped_under_each_with_its_branch()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        // A worktree made with git worktree add, beside the checkout.
        var sibling = Path.Combine(h.Root, "work-topic");
        FakeGitFolders.Repository(sibling, "topic");
        var tab = await h.OpenTabAsync();
        var files = new[] { Path.Combine(h.WorkFolder, "a.cs"), Path.Combine(worktree, "src", "b.cs"), Path.Combine(sibling, "c.cs"), Path.Combine(h.WorkFolder, "d.cs") };
        for (var i = 0; i < files.Length; i++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(files[i])!);
            await File.WriteAllTextAsync(files[i], "b\n", TestContext.Current.CancellationToken);
            EmitEdit(h, $"e{i}", files[i]);
        }

        await TabTestHarness.Eventually(() => tab.ChangedFiles.Files.Count == 4, "the changed files");
        Assert.Equal(
            [("work · main", "a.cs"), (null, "d.cs"), ("brisk-otter · worktree-brisk-otter", "src/b.cs"), ("work-topic · topic", "c.cs")],
            tab.ChangedFiles.Files.Select(r => (r.Group, r.DisplayPath)));
        Assert.Contains(worktree, h.Services.Worktrees.ChangedWorktrees());
    }

    [Fact]
    public async Task One_working_tree_has_no_heading()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var tab = await h.OpenTabAsync();
        var path = Path.Combine(h.WorkFolder, "a.cs");
        await File.WriteAllTextAsync(path, "b\n", TestContext.Current.CancellationToken);

        EmitEdit(h, "e1", path);

        await TabTestHarness.Eventually(() => tab.ChangedFiles.Files is [{ Group: null, DisplayPath: "a.cs" }], "the changed file");
    }

    [Fact]
    public async Task A_removed_worktrees_files_stay_together_under_it_rather_than_deleted()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        var tab = await h.OpenTabAsync();
        var (home, there) = (Path.Combine(h.WorkFolder, "a.cs"), Path.Combine(worktree, "b.cs"));
        await File.WriteAllTextAsync(home, "b\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(there, "b\n", TestContext.Current.CancellationToken);
        EmitEdit(h, "e1", home);
        EmitEdit(h, "e2", there);
        await TabTestHarness.Eventually(() => tab.ChangedFiles.Files.Count == 2, "the changed files");

        Directory.Delete(worktree, recursive: true);
        await tab.ChangedFiles.RefreshCommand.ExecuteAsync(null);

        var row = tab.ChangedFiles.Files.Single(r => r.FileName == "b.cs");
        Assert.Equal(("brisk-otter · removed", "M", "+1 −1", null), (row.Group, row.Status, row.Stats, row.Since));
        Assert.True(row.IsMissing);
        Assert.EndsWith("in a worktree that's gone", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_working_tree_view_lists_each_working_tree_Claude_changed_files_in()
    {
        var git = new FakeLauncher
        {
            OnStart = (spec, process) =>
            {
                if (spec.Arguments.Contains("--show-toplevel"))
                {
                    process.WriteOutput(spec.WorkingDirectory!);
                    process.WriteOutput("");
                }
                else if (spec.Arguments.Contains("status"))
                {
                    process.WriteOutput($"?? {(spec.WorkingDirectory!.EndsWith("brisk-otter", StringComparison.Ordinal) ? "theirs.txt" : "mine.txt")}\0");
                }
                process.Exit(0);
            },
        };
        await using var h = new TabTestHarness(launcher: git);
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        var tab = await h.OpenTabAsync();
        var path = Path.Combine(worktree, "b.cs");
        await File.WriteAllTextAsync(path, "b\n", TestContext.Current.CancellationToken);
        EmitEdit(h, "e1", path);
        await TabTestHarness.Eventually(() => tab.ChangedFiles.Files.Count == 1, "the changed file");

        tab.ChangedFiles.ShowGitChanges = true;

        await TabTestHarness.Eventually(() => tab.ChangedFiles.Files.Count == 2, "both working trees");
        Assert.Equal(
            [("work · main", "mine.txt", null), ("brisk-otter · worktree-brisk-otter", "theirs.txt", worktree)],
            tab.ChangedFiles.Files.Select(r => (r.Group, r.DisplayPath, r.WorkTree)));
    }

    private static string Init(string cwd) => new JsonObject
    {
        ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = "claude-opus-5-5", ["permissionMode"] = "default", ["cwd"] = cwd,
    }.ToJsonString();

    /// <summary>A tool call of the session's own and its successful result.</summary>
    private static void Call(TabTestHarness h, string id, string tool, JsonObject input, JsonObject result)
    {
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = tool, ["input"] = input }) },
        });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = "done" }) },
            ["tool_use_result"] = result,
        });
    }

    /// <summary>A successful Edit of <paramref name="path"/> from <c>a</c> to <c>b</c>.</summary>
    private static void EmitEdit(TabTestHarness h, string id, string path) =>
        Call(h, id, "Edit", new JsonObject { ["file_path"] = path, ["old_string"] = "a", ["new_string"] = "b" },
            new JsonObject { ["filePath"] = path, ["oldString"] = "a", ["newString"] = "b", ["originalFile"] = "a\n" });
}
