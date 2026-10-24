using System.Text.Json.Nodes;
using Claudette.Core.Diffs;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Diffs;

public sealed class ChangedFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"claudette-changed-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ChangedFiles _files;

    public ChangedFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        _files = new ChangedFiles(_time);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathOf(string name) => Path.Combine(_root, "src", name);

    [Fact]
    public void The_first_change_supplies_the_before_side()
    {
        var path = PathOf("auth.cs");
        Edit("t1", path, "one\n");
        _time.Advance(TimeSpan.FromMinutes(2));
        Edit("t2", path, "one changed\n");

        var file = Assert.Single(_files.Files);
        Assert.Equal(path, file.Path);
        Assert.Equal("one\n", file.Before);
        Assert.True(file.BeforeKnown);
        Assert.False(file.IsNew);
        Assert.Equal(2, file.ChangeCount);
        Assert.Equal(["t1", "t2"], file.ToolUseIds);
        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromMinutes(2), file.FirstChanged);
        Assert.Equal(_time.GetUtcNow(), file.LastChanged);
    }

    [Fact]
    public void Files_are_listed_in_order_of_first_change()
    {
        Edit("t1", PathOf("b.cs"), "b");
        Edit("t2", PathOf("a.cs"), "a");
        Edit("t3", PathOf("b.cs"), "b2");

        Assert.Equal(["b.cs", "a.cs"], _files.Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void A_write_that_creates_a_file_is_new()
    {
        Use("t1", "Write", PathOf("new.cs"));
        _files.RecordToolResult("t1", false, JsonNode.Parse($$"""{"type":"create","filePath":{{Json(PathOf("new.cs"))}},"content":"x\n","structuredPatch":[],"originalFile":null}"""));

        var file = Assert.Single(_files.Files);
        Assert.True(file.IsNew);
        Assert.True(file.BeforeKnown);
        Assert.Null(file.Before);
    }

    [Fact]
    public void A_write_that_replaces_a_file_keeps_the_original()
    {
        Use("t1", "Write", PathOf("a.cs"));
        _files.RecordToolResult("t1", false, JsonNode.Parse($$"""{"type":"update","filePath":{{Json(PathOf("a.cs"))}},"content":"new","originalFile":"old\n"}"""));

        Assert.Equal("old\n", Assert.Single(_files.Files).Before);
    }

    [Fact]
    public void A_write_over_a_file_too_large_to_diff_has_an_unknown_before()
    {
        Use("t1", "Write", PathOf("big.txt"));
        _files.RecordToolResult("t1", false, JsonNode.Parse("""{"type":"update","originalFile":null,"structuredPatch":[]}"""));

        var file = Assert.Single(_files.Files);
        Assert.False(file.IsNew);
        Assert.False(file.BeforeKnown);
    }

    [Fact]
    public void An_edit_that_creates_a_file_is_new()
    {
        Use("t1", "Edit", PathOf("a.cs"));
        _files.RecordToolResult("t1", false, JsonNode.Parse("""{"oldString":"","newString":"x","originalFile":null}"""));

        Assert.True(Assert.Single(_files.Files).IsNew);
    }

    [Fact]
    public void An_edit_to_a_file_over_10000_characters_has_an_unknown_before_not_a_new_file()
    {
        // As Claude Code 2.1.284 writes it to the transcript: without the large file's content (DESIGN.md §8).
        Use("t1", "Edit", PathOf("big.cs"));
        _files.RecordToolResult("t1", false, JsonNode.Parse("""{"oldString":"a","newString":"b","originalFile":null,"structuredPatch":[],"contentNotInModelContext":true}"""));

        var file = Assert.Single(_files.Files);
        Assert.False(file.IsNew);
        Assert.False(file.BeforeKnown);
    }

    [Theory]
    [InlineData("""{"stdout":"","stderr":""}""")]
    [InlineData("\"The file was updated.\"")]
    [InlineData(null)]
    public void A_result_without_original_file_still_counts_with_an_unknown_before(string? json)
    {
        Use("t1", "MultiEdit", PathOf("a.cs"));
        _files.RecordToolResult("t1", false, json is null ? null : JsonNode.Parse(json));

        var file = Assert.Single(_files.Files);
        Assert.False(file.BeforeKnown);
        Assert.False(file.IsNew);
        Assert.Null(file.Before);
    }

    [Fact]
    public void Notebook_edits_use_their_own_field_names()
    {
        var path = PathOf("analysis.ipynb");
        _files.RecordToolUse("t1", "NotebookEdit", new JsonObject { ["notebook_path"] = path, ["new_source"] = "print(1)" });
        _files.RecordToolResult("t1", false, new JsonObject { ["notebook_path"] = path, ["original_file"] = "{}", ["updated_file"] = "{\"cells\":[]}" });

        var file = Assert.Single(_files.Files);
        Assert.Equal(path, file.Path);
        Assert.Equal("{}", file.Before);
    }

    [Fact]
    public void Error_results_and_failed_notebook_edits_are_ignored()
    {
        Use("t1", "Edit", PathOf("a.cs"));
        _files.RecordToolResult("t1", true, null);
        _files.RecordToolUse("t2", "NotebookEdit", new JsonObject { ["notebook_path"] = PathOf("n.ipynb") });
        _files.RecordToolResult("t2", false, new JsonObject { ["error"] = "Cell not found", ["original_file"] = "{}" });

        Assert.Empty(_files.Files);
    }

    [Fact]
    public void Other_tools_and_unknown_results_are_ignored()
    {
        _files.RecordToolUse("t1", "Read", new JsonObject { ["file_path"] = PathOf("a.cs") });
        _files.RecordToolResult("t1", false, new JsonObject { ["filePath"] = PathOf("a.cs"), ["originalFile"] = "x" });
        _files.RecordToolResult("never-used", false, new JsonObject { ["filePath"] = PathOf("b.cs"), ["originalFile"] = "x" });

        Assert.Empty(_files.Files);
    }

    [Fact]
    public void A_result_that_names_another_calls_file_is_not_this_ones()
    {
        // A message has one structured result: with two tool results in it, only the call whose file it names gets it.
        Use("t1", "Edit", PathOf("a.cs"));
        Use("t2", "Edit", PathOf("b.cs"));
        var result = new JsonObject { ["filePath"] = PathOf("a.cs"), ["oldString"] = "a", ["newString"] = "b", ["originalFile"] = "a's\n" };
        _files.RecordToolResult("t1", false, result);
        _files.RecordToolResult("t2", false, result);

        Assert.Equal([("a.cs", true, "a's\n"), ("b.cs", false, null)],
            _files.Files.Select(f => (Path.GetFileName(f.Path), f.BeforeKnown, f.Before)));
    }

    [Fact]
    public void Replaying_the_same_events_does_not_count_twice()
    {
        var changed = 0;
        _files.Changed += () => changed++;
        for (var replay = 0; replay < 2; replay++)
        {
            Edit("t1", PathOf("a.cs"), "one\n");
            Edit("t2", PathOf("a.cs"), "two\n");
        }

        var file = Assert.Single(_files.Files);
        Assert.Equal(2, file.ChangeCount);
        Assert.Equal("one\n", file.Before);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Replay_can_supply_the_time_of_each_change()
    {
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Use("t1", "Edit", PathOf("a.cs"));
        _files.RecordToolResult("t1", false, new JsonObject { ["originalFile"] = "x" }, at);

        Assert.Equal(at, Assert.Single(_files.Files).FirstChanged);
    }

    [Fact]
    public void Paths_match_by_the_platform_rule()
    {
        var insensitive = new ChangedFiles(_time, StringComparer.OrdinalIgnoreCase);
        var sensitive = new ChangedFiles(_time, StringComparer.Ordinal);
        foreach (var files in new[] { insensitive, sensitive })
        {
            files.RecordToolUse("t1", "Edit", new JsonObject { ["file_path"] = PathOf("Auth.cs") });
            files.RecordToolResult("t1", false, new JsonObject { ["originalFile"] = "x" });
            files.RecordToolUse("t2", "Edit", new JsonObject { ["file_path"] = PathOf("auth.cs") });
            files.RecordToolResult("t2", false, new JsonObject { ["originalFile"] = "y" });
        }

        Assert.Equal(2, Assert.Single(insensitive.Files).ChangeCount);
        Assert.Equal(2, sensitive.Files.Count);
        Assert.Same(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase,
            ChangedFiles.PlatformPathComparer);
    }

    [Fact]
    public void Paths_are_matched_after_normalizing()
    {
        Edit("t1", PathOf("a.cs"), "x");
        Edit("t2", Path.Combine(_root, "src", "..", "src", "a.cs"), "y");

        Assert.Equal(2, Assert.Single(_files.Files).ChangeCount);
    }

    [Fact]
    public void Inspect_counts_the_lines_changed_since_before()
    {
        var path = PathOf("a.cs");
        Edit("t1", path, "one\ntwo\nthree\n");
        File.WriteAllText(path, "one\n2\nthree\nfour\n");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Modified, state.Status);
        Assert.Equal(2, state.Added);
        Assert.Equal(1, state.Removed);
        Assert.Equal("one\n2\nthree\nfour\n", state.Current);
    }

    [Fact]
    public void Inspect_sees_a_file_put_back_as_unchanged_even_with_other_line_endings()
    {
        var path = PathOf("a.cs");
        Edit("t1", path, "one\ntwo\n");
        File.WriteAllText(path, "one\r\ntwo\r\n");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Unchanged, state.Status);
        Assert.Equal((0, 0), (state.Added, state.Removed));
    }

    [Fact]
    public void Inspect_counts_a_new_file_as_added()
    {
        var path = PathOf("new.cs");
        Use("t1", "Write", path);
        _files.RecordToolResult("t1", false, new JsonObject { ["type"] = "create", ["originalFile"] = null });
        File.WriteAllText(path, "a\nb\nc");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Added, state.Status);
        Assert.Equal((3, 0), (state.Added, state.Removed));
    }

    [Fact]
    public void Inspect_reports_a_missing_file_as_deleted()
    {
        // The helper's Edit replaces "a", which isn't in the file, so what it left isn't known: the disk is all there is.
        Edit("t1", PathOf("gone.cs"), "x\ny\n");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Deleted, state.Status);
        Assert.Equal((0, 2), (state.Added, state.Removed));
        Assert.Null(state.Current);
    }

    [Fact]
    public void Inspect_keeps_the_sessions_change_to_a_file_deleted_since()
    {
        Edit("t1", PathOf("gone.cs"), "a\nb\n");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Modified, state.Status);
        Assert.Equal((1, 1), (state.Added, state.Removed));
        Assert.Equal(new ChangedSince(true, null, null), state.Since);
    }

    [Fact]
    public void Inspect_treats_a_new_file_that_was_removed_again_as_unchanged()
    {
        Use("t1", "Write", PathOf("temp.cs"));
        _files.RecordToolResult("t1", false, new JsonObject { ["type"] = "create" });

        Assert.Equal(ChangedFileStatus.Unchanged, _files.Inspect(Assert.Single(_files.Files)).Status);
    }

    [Fact]
    public void Inspect_does_not_count_lines_of_binary_or_very_large_files()
    {
        var binary = PathOf("image.png");
        var large = PathOf("large.txt");
        File.WriteAllBytes(binary, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);
        File.WriteAllText(large, new string('x', 2_000));
        var files = new ChangedFiles(_time) { MaxInspectBytes = 1_000 };
        foreach (var (id, path) in new[] { ("t1", binary), ("t2", large) })
        {
            files.RecordToolUse(id, "Edit", new JsonObject { ["file_path"] = path });
            files.RecordToolResult(id, false, new JsonObject { ["originalFile"] = "old" });
        }

        var binaryState = files.Inspect(files.Files[0]);
        var largeState = files.Inspect(files.Files[1]);

        Assert.True(binaryState.IsBinary);
        Assert.Equal(ChangedFileStatus.Modified, binaryState.Status);
        Assert.Null(binaryState.Added);
        Assert.Null(binaryState.Current);
        Assert.True(largeState.IsTooLarge);
        Assert.Null(largeState.Removed);
    }

    [Fact]
    public void Inspect_cannot_count_without_a_known_before()
    {
        var path = PathOf("a.cs");
        Use("t1", "Edit", path);
        _files.RecordToolResult("t1", false, new JsonObject { ["filePath"] = path });
        File.WriteAllText(path, "now\n");

        var state = _files.Inspect(Assert.Single(_files.Files));

        Assert.Equal(ChangedFileStatus.Modified, state.Status);
        Assert.Null(state.Added);
        Assert.Equal("now\n", state.Current);
    }

    [Fact]
    public void Display_paths_are_relative_inside_the_folder_and_full_outside_it()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"claudette-outside-{Guid.NewGuid():N}", "x.cs");
        Edit("t1", PathOf("auth.cs"), "a");
        Edit("t2", outside, "b");

        Assert.Equal("src/auth.cs", _files.DisplayPath(_files.Files[0], _root));
        Assert.Equal("src/auth.cs", _files.DisplayPath(_files.Files[0], _root + Path.DirectorySeparatorChar));
        Assert.Equal("auth.cs", _files.DisplayPath(_files.Files[0], Path.Combine(_root, "src")));
        Assert.Equal(outside.Replace('\\', '/'), _files.DisplayPath(_files.Files[1], _root));
        Assert.Equal(PathOf("auth.cs").Replace('\\', '/'), _files.DisplayPath(_files.Files[0], Path.Combine(_root, "sr")));
    }

    [Fact]
    public void Display_paths_follow_the_case_rule()
    {
        var insensitive = new ChangedFiles(_time, StringComparer.OrdinalIgnoreCase);
        insensitive.RecordToolUse("t1", "Edit", new JsonObject { ["file_path"] = PathOf("a.cs") });
        insensitive.RecordToolResult("t1", false, new JsonObject { ["originalFile"] = "x" });

        Assert.Equal("src/a.cs", insensitive.DisplayPath(insensitive.Files[0], _root.ToUpperInvariant()));
    }

    // ---- Changes per turn (DESIGN.md §8) ---------------------------------------------------------------------------

    [Fact]
    public void Each_turn_hands_over_the_files_it_changed_with_what_they_held_before_it()
    {
        var auth = PathOf("auth.cs");
        Edit("t1", auth, "one\n");
        Edit("t2", auth, "one changed\n");
        Use("t3", "Write", PathOf("new.cs"));
        _files.RecordToolResult("t3", false, JsonNode.Parse($$"""{"type":"create","filePath":{{Json(PathOf("new.cs"))}},"content":"x\n","structuredPatch":[],"originalFile":null}"""));

        var first = _files.TakeTurn();

        // A file once, before the turn's first change to it, in order of first change.
        Assert.Equal([(auth, "one\n", false, true), (PathOf("new.cs"), (string?)null, true, true)], first.Select(c => (c.Path, c.Before, c.IsNew, c.BeforeKnown)));

        Edit("t4", auth, "two\n");
        var second = Assert.Single(_files.TakeTurn());
        // Before the second turn: how the first left it, while the list keeps the session's first "before".
        Assert.Equal("two\n", second.Before);
        Assert.Equal("one\n", Assert.Single(_files.Files, f => f.Path == auth).Before);
        Assert.Empty(_files.TakeTurn());
    }

    [Fact]
    public void A_transcript_read_back_has_no_turn_to_hand_over()
    {
        Use("t1", "Edit", PathOf("a.cs"));
        _files.RecordToolResult("t1", false, new JsonObject { ["filePath"] = PathOf("a.cs"), ["originalFile"] = "a", ["oldString"] = "a" }, inTurn: false);

        Assert.Single(_files.Files);
        Assert.Empty(_files.TakeTurn());
    }

    [Fact]
    public void A_turns_change_whose_after_isnt_known_is_counted_against_the_file_as_it_left_it()
    {
        var path = PathOf("count.cs");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        // The helper's Edit replaces "a", which isn't in the file, so what it left isn't known.
        Edit("t1", path, "one\n");

        var change = Assert.Single(_files.TakeTurn());

        Assert.Equal((2, 0), _files.CountTurn(change));
        Assert.Null(_files.CountTurn(change with { BeforeKnown = false, Before = null }));
        File.Delete(path);
        Assert.Equal((0, 1), _files.CountTurn(change));
    }

    [Fact]
    public void A_files_changes_end_where_the_sessions_latest_change_left_it()
    {
        var path = PathOf("own.cs");
        EditTo("t1", path, "a\n", "a", "b");
        EditTo("t2", path, "b\n", "b", "c");
        // Another tab adds a line afterwards.
        File.WriteAllText(path, "c\nanother tab's line\n");

        var file = Assert.Single(_files.Files);
        Assert.Equal(("c\n", true, false), (file.After, file.AfterKnown, file.ChangedBetween));
        var state = _files.Inspect(file);
        Assert.Equal((ChangedFileStatus.Modified, 1, 1), (state.Status, state.Added, state.Removed));
        Assert.Equal(new ChangedSince(false, 1, 0), state.Since);
    }

    [Fact]
    public void A_file_as_the_sessions_change_left_it_has_nothing_since()
    {
        var path = PathOf("same.cs");
        EditTo("t1", path, "a\n", "a", "b");
        File.WriteAllText(path, "b\r\n");

        Assert.Null(_files.Inspect(Assert.Single(_files.Files)).Since);
    }

    [Fact]
    public void A_change_that_finds_the_file_changed_since_the_one_before_notes_it()
    {
        var path = PathOf("between.cs");
        EditTo("t1", path, "a\n", "a", "b");
        // Something else added a line before the second edit found the file.
        EditTo("t2", path, "b\nx\n", "b", "c");

        var file = Assert.Single(_files.Files);
        Assert.True(file.ChangedBetween);
        Assert.Equal("c\nx\n", file.After);
    }

    [Fact]
    public void A_large_file_read_back_is_followed_from_its_saved_before_through_each_edit()
    {
        var path = PathOf("large.cs");
        var befores = new BeforeContentStore(Path.Combine(_root, "befores"), _time);
        var files = new ChangedFiles(_time) { Befores = befores };
        var large = string.Concat(Enumerable.Range(1, 400).Select(i => $"line {i:D4} of a large file\n"));
        befores.Save("t1", large);

        // As a transcript has them: originalFile left out, over 10,000 characters.
        foreach (var (id, from, to) in new[] { ("t1", "line 0007", "line seven"), ("t2", "line 0008", "line eight") })
        {
            files.RecordToolUse(id, "Edit", new JsonObject { ["file_path"] = path, ["old_string"] = from, ["new_string"] = to });
            files.RecordToolResult(id, false, new JsonObject { ["filePath"] = path, ["oldString"] = from, ["newString"] = to, ["originalFile"] = null }, inTurn: false);
        }

        var file = Assert.Single(files.Files);
        Assert.True(file.AfterKnown);
        Assert.Equal(large.Replace("line 0007", "line seven", StringComparison.Ordinal).Replace("line 0008", "line eight", StringComparison.Ordinal), file.After);
    }

    [Fact]
    public void A_turn_is_counted_to_how_its_last_change_left_the_file_not_the_disk()
    {
        var path = PathOf("turn.cs");
        File.WriteAllText(path, "b\nanother tab's line\n");
        EditTo("t1", path, "a\n", "a", "b");

        var change = Assert.Single(_files.TakeTurn());

        Assert.Equal(("b\n", true), (change.After, change.AfterKnown));
        Assert.Equal((1, 1), _files.CountTurn(change));
    }

    [Fact]
    public void A_subagents_change_takes_its_before_and_real_path_from_the_hook()
    {
        var befores = new BeforeContentStore(Path.Combine(_root, "befores"), _time);
        var files = new ChangedFiles(_time) { Befores = befores };
        var (edited, created) = (PathOf("sub.cs"), PathOf("made.cs"));

        // The hook reads each file before its call runs; the subagent's results say nothing of it.
        files.NoteBefore("s1", edited, "a\n", known: true);
        files.RecordToolUse("s1", "Edit", new JsonObject { ["file_path"] = edited, ["old_string"] = "a", ["new_string"] = "b" });
        files.RecordToolResult("s1", false, null);
        files.NoteBefore("s2", created, null, known: true);
        // The model named it relative to the subagent's folder; the hook has its real path.
        files.RecordToolUse("s2", "Write", new JsonObject { ["file_path"] = "made.cs", ["content"] = "new\n" });
        files.RecordToolResult("s2", false, null);

        Assert.Equal([(edited, "a\n", false, "b\n"), (created, null, true, "new\n")],
            files.Files.Select(f => (f.Path, f.Before, f.IsNew, f.After)));
        Assert.All(files.Files, f => Assert.True(f.BeforeKnown && f.AfterKnown));
    }

    [Fact]
    public void A_subagents_first_change_is_found_again_when_its_transcript_is_read_back()
    {
        var befores = new BeforeContentStore(Path.Combine(_root, "befores"), _time);
        var live = new ChangedFiles(_time) { Befores = befores };
        var (edited, created) = (PathOf("sub.cs"), PathOf("made.cs"));
        live.NoteBefore("s1", edited, "a\n", known: true);
        live.NoteBefore("s2", created, null, known: true);

        // Read back: no hook then, and the transcript has no result for a subagent's call either.
        var replayed = new ChangedFiles(_time) { Befores = befores };
        replayed.RecordToolUse("s1", "Edit", new JsonObject { ["file_path"] = edited, ["old_string"] = "a", ["new_string"] = "b" });
        replayed.RecordToolResult("s1", false, null, inTurn: false);
        replayed.RecordToolUse("s2", "Write", new JsonObject { ["file_path"] = created, ["content"] = "new\n" });
        replayed.RecordToolResult("s2", false, null, inTurn: false);

        Assert.Equal([("a\n", false, true, "b\n"), (null, true, true, "new\n")],
            replayed.Files.Select(f => (f.Before, f.IsNew, f.BeforeKnown, f.After)));
    }

    /// <summary>An Edit of <paramref name="path"/> from <paramref name="originalFile"/>, replacing <paramref name="from"/> with <paramref name="to"/>.</summary>
    private void EditTo(string id, string path, string originalFile, string from, string to)
    {
        _files.RecordToolUse(id, "Edit", new JsonObject { ["file_path"] = path, ["old_string"] = from, ["new_string"] = to });
        _files.RecordToolResult(id, false, new JsonObject { ["filePath"] = path, ["oldString"] = from, ["newString"] = to, ["originalFile"] = originalFile });
    }

    private void Use(string id, string tool, string path) =>
        _files.RecordToolUse(id, tool, new JsonObject { ["file_path"] = path });

    private void Edit(string id, string path, string originalFile)
    {
        Use(id, "Edit", path);
        _files.RecordToolResult(id, false, new JsonObject
        {
            ["filePath"] = path,
            ["oldString"] = "a",
            ["newString"] = "b",
            ["originalFile"] = originalFile,
            ["structuredPatch"] = new JsonArray(),
        });
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
