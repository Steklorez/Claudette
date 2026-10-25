using Claudette.App.Diffs;
using Claudette.App.Tests.Support;
using Claudette.Core.Diffs;

namespace Claudette.App.Tests;

/// <summary>The diff view on what changed that the lines don't show (DESIGN.md §8, "What the lines don't show").</summary>
public sealed class DiffHiddenChangesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"claudette-hidden-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<DiffWindowViewModel> OpenAsync(string path, string? before, Func<string?, HiddenChanges?>? hidden = null, bool fromGit = false)
    {
        var source = new DiffSource(path, Path.GetFileName(path), before, "before Claude's first change", null,
            () => Task.FromResult(false), () => Task.CompletedTask, () => Task.CompletedTask, FromGit: fromGit,
            Hidden: hidden ?? (text => HiddenChanges.Between(before, null, text, null)));
        var view = new DiffWindowViewModel(source, dark: false);
        await TabTestHarness.Eventually(() => !view.IsLoading, "the diff");
        return view;
    }

    private string Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task Whitespace_shows_on_lines_that_changed_only_in_it()
    {
        var path = Write("a.cs", "a\n    b\nC\n"u8.ToArray());

        var view = await OpenAsync(path, "a\n\tb\nc\n");

        var changed = view.InlineRows.Where(r => r.IsAdded || r.IsRemoved).Select(r => (r.Marker, r.Text)).ToArray();
        Assert.Equal([("−", "→b"), ("−", "c"), ("+", "····b"), ("+", "C")], changed);
        Assert.Equal("Where a line changed only in its whitespace, spaces show as · and tabs as →", view.Note);
        view.IsSideBySide = true;
        Assert.Contains(view.SideRows, r => r is { Left.Text: "→b", Right.Text: "····b" });
    }

    [Fact]
    public async Task A_file_that_changed_only_in_whitespace_says_so()
    {
        var path = Write("a.cs", "a\nb\n"u8.ToArray());

        var view = await OpenAsync(path, "a \nb\n");

        Assert.Equal("+1 −1", view.Stats);
        Assert.Equal("Only whitespace changed: spaces show as · and tabs as →", view.Note);
        Assert.Contains(view.InlineRows, r => r is { IsRemoved: true, Text: "a·" });
    }

    [Fact]
    public async Task No_line_changed_says_what_did_in_their_place()
    {
        var path = Write("a.cs", "a\nb"u8.ToArray());

        var view = await OpenAsync(path, "a\nb\n");

        Assert.Empty(view.InlineRows);
        Assert.Equal("newline at end", view.Stats);
        Assert.Equal("Only the newline at its end changed: removed. The lines are the same on both sides.", view.Message);
        Assert.Null(view.Note);
    }

    [Fact]
    public async Task Lines_and_line_endings_both_changed_note_the_line_endings()
    {
        var path = Write("a.cs", "a\nB\n"u8.ToArray());

        var view = await OpenAsync(path, "a\nb\n", text => HiddenChanges.Between("a\nb\n", Core.ProjectFiles.LineEndingKind.Crlf, text, Core.ProjectFiles.LineEndingKind.Lf));

        Assert.Equal("+1 −1", view.Stats);
        Assert.Equal("Its line endings changed too: CRLF to LF", view.Note);
        Assert.Null(view.Message);
    }

    [Fact]
    public async Task A_git_file_whose_lines_are_the_same_says_git_counts_something_else()
    {
        var path = Write("a.cs", "a\nb\n"u8.ToArray());

        var view = await OpenAsync(path, "a\nb\n", _ => null, fromGit: true);

        Assert.Equal("The lines are the same as at HEAD. Git counts something else as changed, such as its byte order mark or encoding.", view.Message);
    }

    [Fact]
    public async Task Reverting_a_hunk_of_a_file_with_a_byte_order_mark_keeps_it()
    {
        // The view reads the mark as Claude Code's originalFile has it, a leading ﻿; the file keeps its own.
        var path = Write("a.cs", [0xEF, 0xBB, 0xBF, .. "one\nTWO\nthree\n"u8]);
        var view = await OpenAsync(path, "﻿one\ntwo\nthree\n");
        Assert.Null(view.Message);

        await view.RevertHunkCommand.ExecuteAsync(view.InlineRows.Single(r => r.CanRevert).Hunk);

        Assert.Equal([0xEF, 0xBB, 0xBF, .. "one\ntwo\nthree\n"u8], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("Nothing has changed: the lines are the same on both sides.", view.Message);
    }
}
