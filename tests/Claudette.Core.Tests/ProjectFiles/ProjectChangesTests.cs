using Claudette.Core.Diffs;
using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>Several tabs' changed files, merged for the Project Files tab (DESIGN.md §18, "Project files", "Claude's changes").</summary>
public sealed class ProjectChangesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "claudette-project-changes");
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

    private static string PathOf(string name) => Path.Combine(Root, name);

    private static DateTimeOffset At(int minutes) => Start.AddMinutes(minutes);

    [Fact]
    public void One_tabs_changes_are_its_own()
    {
        var file = Changed("Player.cpp", "old", "t1", At(5));
        file.AddChange("t2", At(9));

        var change = Assert.Single(ProjectChanges.Merge([new TabChanges("tab1", "refactor auth", [file])])).Value;

        Assert.Equal(PathOf("Player.cpp"), change.Path);
        Assert.Equal(("old", true, false), (change.Before, change.BeforeKnown, change.IsNew));
        var author = Assert.Single(change.Authors);
        Assert.Equal(new ChangeAuthor("tab1", "refactor auth", At(9), "t2"), author);
        Assert.Equal("refactor auth", change.AuthorNames);
        Assert.Equal(At(9), change.LastChanged);
    }

    [Fact]
    public void Two_tabs_changing_one_file_show_against_the_earliest_before()
    {
        // The second tab changed it first, so its "before" is what the file held before either.
        var later = Changed("Player.cpp", "after the fix", "a1", At(10));
        var earlier = Changed("Player.cpp", "original", "b1", At(2));
        earlier.AddChange("b2", At(4));

        var merged = ProjectChanges.Merge([new TabChanges("tab1", "refactor auth", [later]), new TabChanges("tab2", "fix login", [earlier])]);

        var change = Assert.Single(merged).Value;
        Assert.Equal("original", change.Before);
        // Latest first.
        Assert.Equal([("tab1", "a1"), ("tab2", "b2")], change.Authors.Select(a => (a.TabId, a.LatestChange)));
        Assert.Equal("refactor auth and fix login", change.AuthorNames);
        Assert.Equal(At(10), change.LastChanged);
    }

    [Fact]
    public void A_file_one_tab_created_is_new_though_another_edited_it_after()
    {
        var created = Changed("New.cs", null, "a1", At(1), isNew: true);
        var edited = Changed("New.cs", "as created", "b1", At(3));

        var change = ProjectChanges.Merge([new TabChanges("tab2", "tidy", [edited]), new TabChanges("tab1", "add New", [created])])[PathOf("New.cs")];

        Assert.True(change.IsNew);
        Assert.True(change.BeforeKnown);
        Assert.Null(change.Before);
    }

    [Fact]
    public void An_unknown_before_comes_with_the_earliest_change()
    {
        var unknown = Changed("Big.json", null, "a1", At(1), beforeKnown: false);
        var known = Changed("Big.json", "{}", "b1", At(2));

        var change = Assert.Single(ProjectChanges.Merge([new TabChanges("b", "B", [known]), new TabChanges("a", "A", [unknown])])).Value;

        Assert.False(change.BeforeKnown);
        Assert.Null(change.Before);
    }

    [Fact]
    public void Different_files_stay_apart()
    {
        var merged = ProjectChanges.Merge(
        [
            new TabChanges("tab1", "one", [Changed("a.txt", "a", "a1", At(1)), Changed("b.txt", "b", "a2", At(2))]),
            new TabChanges("tab2", "two", [Changed("c.txt", "c", "b1", At(3))]),
        ]);

        Assert.Equal(3, merged.Count);
        Assert.Equal("two", merged[PathOf("c.txt")].AuthorNames);
        Assert.Empty(ProjectChanges.Merge([]));
    }

    [Fact]
    public void Paths_are_matched_once_made_full_and_as_the_comparer_says()
    {
        var spelled = new ChangedFile(Path.Combine(Root, "src", "..", "Player.cpp"), "x", isNew: false, beforeKnown: true, "a1", At(1));
        var upper = new ChangedFile(PathOf("PLAYER.CPP"), "y", isNew: false, beforeKnown: true, "b1", At(2));
        var plain = Changed("Player.cpp", "z", "c1", At(3));
        TabChanges[] tabs = [new("a", "A", [spelled]), new("b", "B", [upper]), new("c", "C", [plain])];

        var ignoringCase = ProjectChanges.Merge(tabs, StringComparer.OrdinalIgnoreCase);
        var byCase = ProjectChanges.Merge(tabs, StringComparer.Ordinal);

        var merged = Assert.Single(ignoringCase).Value;
        Assert.Equal(PathOf("Player.cpp"), merged.Path);
        Assert.Equal("x", merged.Before);
        Assert.Equal("C, B and A", merged.AuthorNames);
        Assert.Equal(2, byCase.Count);
        Assert.Equal(["A", "C"], byCase[PathOf("Player.cpp")].Authors.Select(a => a.TabName).Order());
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "one")]
    [InlineData(2, "one and two")]
    [InlineData(4, "one, two, three and four")]
    public void Authors_are_named_in_a_list(int count, string names)
    {
        string[] all = ["one", "two", "three", "four"];
        var authors = all.Take(count).Select((name, i) => new ChangeAuthor($"tab{i}", name, At(10 - i), null)).ToArray();

        Assert.Equal(names, new ProjectFileChange("/x", null, false, false, authors).AuthorNames);
    }

    [Fact]
    public void A_file_no_tab_changed_has_no_last_change()
    {
        Assert.Equal(default(DateTimeOffset), new ProjectFileChange("/x", null, false, false, []).LastChanged);
    }

    private static ChangedFile Changed(string name, string? before, string toolUseId, DateTimeOffset at, bool isNew = false, bool beforeKnown = true) =>
        new(PathOf(name), before, isNew, beforeKnown, toolUseId, at);
}
