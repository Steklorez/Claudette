using System.Text;
using Claudette.Core.ProjectFiles;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>Find in files and Replace in files (DESIGN.md §18, "Project files", "Finding in files").</summary>
public sealed class FindInFilesTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-find");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FindMatcher Matcher(string text, bool matchCase = false, bool words = false, bool regex = false) =>
        FindMatcher.Create(new FindQuery(text, matchCase, words, regex));

    /// <summary>The matches in one line, as (start, length).</summary>
    private static List<(int, int)> Find(FindMatcher matcher, string line)
    {
        var found = new List<(int Start, int Length)>();
        matcher.Find(line, found);
        return [.. found.Select(f => (f.Start, f.Length))];
    }

    /// <summary>Every file under the folder, relative with forward slashes and in order, as the <c>@</c> list has them.</summary>
    private IReadOnlyList<string> Files() =>
        [.. Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_temp.Path, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private FindRequest Request(FindMatcher matcher) => new(_temp.Path, Files(), matcher);

    private static async Task<(List<FindHit> Hits, FindBatch Last, int Batches)> SearchAsync(FindRequest request)
    {
        var hits = new List<FindHit>();
        FindBatch? last = null;
        var batches = 0;
        await foreach (var batch in FindInFiles.SearchAsync(request, Token))
        {
            hits.AddRange(batch.Hits);
            last = batch;
            batches++;
        }
        Assert.True(last!.IsComplete);
        return (hits, last, batches);
    }

    // ---- Matching ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Case_is_ignored_unless_Match_case_is_on()
    {
        Assert.Equal([(5, 4), (12, 4)], Find(Matcher("dash"), "void Dash() DASH"));
        Assert.Equal([(5, 4)], Find(Matcher("Dash", matchCase: true), "void Dash() DASH"));
    }

    [Fact]
    public void Words_finds_only_matches_that_start_and_end_at_a_words_edge()
    {
        var words = Matcher("Dash", words: true);

        Assert.Equal([(0, 4), (14, 4)], Find(words, "Dash DashCost Dash() _Dash xDash"));
        // What starts or ends with a character that isn't a word's needs no edge there.
        Assert.Equal([(1, 2)], Find(Matcher("-x", words: true), "a-x b"));
    }

    [Fact]
    public void A_regular_expression_matches_within_a_line_and_never_an_empty_match()
    {
        Assert.Equal([(6, 15)], Find(Matcher(@"Spend\((\w+)\)", regex: true), "    { Spend(DashCost); }"));
        Assert.Equal([(1, 2), (5, 1)], Find(Matcher("x*", regex: true), "axx bx"));
        // Words, with a regular expression too.
        Assert.Equal([(0, 4)], Find(Matcher("Da.h", words: true, regex: true), "Dash Dashed"));
    }

    [Fact]
    public void A_regular_expression_that_isnt_valid_says_why_and_nothing_typed_isnt_a_problem()
    {
        Assert.False(FindMatcher.TryCreate(new FindQuery("Spend(", IsRegex: true), out _, out var problem));
        Assert.StartsWith("That isn't a regular expression", problem, StringComparison.Ordinal);
        Assert.False(FindMatcher.TryCreate(new FindQuery(""), out _, out var none));
        Assert.Null(none);
        Assert.False(FindMatcher.TryCreate(new FindQuery("a\nb"), out _, out var lineBreak));
        Assert.Contains("within lines", lineBreak, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_end_as_the_editor_ends_them_and_columns_count_from_one()
    {
        var hits = FindInFiles.Search("p", "p", "one\r\ntwo\rthree\nfour two\n", Matcher("two"), int.MaxValue, out var count);

        Assert.Equal(2, count);
        Assert.Equal([(2, 1, "two"), (4, 6, "four two")], hits.Select(h => (h.Line, h.Column, h.LineText)));
        // A regular expression can't reach across a line's end.
        Assert.Empty(FindInFiles.Search("p", "p", "one\ntwo", Matcher(@"one\s+two", regex: true), int.MaxValue, out _));
    }

    [Fact]
    public void A_long_line_keeps_only_the_part_around_the_match()
    {
        var line = new string('a', 5_000) + "needle" + new string('b', 5_000);

        var hit = Assert.Single(FindInFiles.Search("p", "p", line, Matcher("needle"), int.MaxValue, out _));

        Assert.Equal(5_001, hit.Column);
        Assert.True(hit.LineText.Length < 1_000);
        Assert.Equal("needle", hit.LineText.Substring(hit.MatchStart, hit.Length));
    }

    [Theory]
    [InlineData("*.cs, *.h", "Source/Player.cs", true)]
    [InlineData("*.cs, *.h", "Source/Player.h", true)]
    [InlineData("*.cs, *.h", "Source/Player.cpp", false)]
    [InlineData("*.cs; !*.Designer.cs", "Forms/Main.Designer.cs", false)]
    [InlineData("*.cs; !*.Designer.cs", "Forms/Main.cs", true)]
    [InlineData("!*.Designer.cs", "README.md", true)]
    [InlineData(".CS", "Player.cs", true)]
    [InlineData("Source/**/*.cpp", "Source/Game/Player/Player.cpp", true)]
    [InlineData("Source/**/*.cpp", "Source/Player.cpp", true)]
    [InlineData("Source/*.cpp", "Source/Game/Player.cpp", false)]
    [InlineData("Player.?pp", "Source/Player.cpp", true)]
    [InlineData("", "anything.bin", true)]
    public void The_file_mask_picks_files_by_name_and_leaves_out_those_after_an_exclamation_mark(string mask, string path, bool searched) =>
        Assert.Equal(searched, FileMask.Parse(mask).Matches(path));

    // ---- Searching files ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Files_are_searched_in_path_order_within_the_scope_and_mask()
    {
        _temp.Write("Source/Player.cpp", "void Dash()\n{\n    Spend(DashCost);\n}\n");
        _temp.Write("Source/Player.h", "void Dash();\n");
        _temp.Write("Docs/Dash.md", "# Dash\n");
        _temp.Write("Source/Enemy.cpp", "void Attack();\n");

        var (all, last, _) = await SearchAsync(Request(Matcher("dash")));
        Assert.Equal(["Docs/Dash.md:1", "Source/Player.cpp:1", "Source/Player.cpp:3", "Source/Player.h:1"], all.Select(h => $"{h.RelativePath}:{h.Line}").Distinct());
        Assert.Equal((4, 3, 4, 4, false), (last.Matches, last.FilesWithMatches, last.FilesSearched, last.FilesToSearch, last.IsCapped));
        Assert.Equal(Path.Combine(_temp.Path, "Source", "Player.cpp"), all[1].Path);

        var (inSource, _, _) = await SearchAsync(Request(Matcher("dash")) with { Under = "Source/", Mask = FileMask.Parse("*.cpp") });
        Assert.Equal(["Source/Player.cpp"], inSource.Select(h => h.RelativePath).Distinct());
    }

    [Fact]
    public async Task Binary_files_and_files_over_10_MB_are_passed_over()
    {
        _temp.Write("text.txt", "needle\n");
        File.WriteAllBytes(_temp.Combine("image.png"), [.. Encoding.ASCII.GetBytes("needle"), 0, 1, 2]);
        File.WriteAllText(_temp.Combine("huge.log"), "needle\n" + new string('x', (int)TextFile.MaxBytes));

        var (hits, last, _) = await SearchAsync(Request(Matcher("needle")));

        Assert.Equal("text.txt", Assert.Single(hits).RelativePath);
        Assert.Equal(3, last.FilesSearched);
    }

    [Fact]
    public async Task An_open_file_is_searched_as_its_editor_has_it_not_as_it_is_on_disk()
    {
        var path = _temp.Write("Player.cpp", "void Dash();\n");
        var open = new Dictionary<string, string>(FindInFiles.PathComparer) { [path] = "void Sprint();\nvoid Dash();\n" };

        var (hits, _, _) = await SearchAsync(Request(Matcher("Dash")) with { OpenTexts = open });

        var hit = Assert.Single(hits);
        Assert.Equal((2, 6), (hit.Line, hit.Column));
    }

    [Fact]
    public async Task Past_the_most_kept_matches_are_still_counted()
    {
        _temp.Write("a.txt", "x x x\n");
        _temp.Write("b.txt", "x x\n");

        var (hits, last, _) = await SearchAsync(Request(Matcher("x")) with { MostHits = 4 });

        Assert.Equal(4, hits.Count);
        Assert.Equal(["a.txt", "a.txt", "a.txt", "b.txt"], hits.Select(h => h.RelativePath));
        Assert.Equal((5, 2, true), (last.Matches, last.FilesWithMatches, last.IsCapped));
    }

    [Fact]
    public async Task Cancelling_a_search_stops_it()
    {
        for (var i = 0; i < 200; i++)
        {
            _temp.Write($"f{i:000}.txt", "needle\n");
        }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var batches = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var batch in FindInFiles.SearchAsync(Request(Matcher("needle")) with { Parallelism = 1 }, cancel.Token))
            {
                batches++;
                await cancel.CancelAsync();
            }
        });
        Assert.Equal(1, batches);
    }

    // ---- Replacing ---------------------------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<FileReplaceResult>> ReplaceAsync(FindMatcher matcher, string replacement, IBeforeFilesWrite? before = null, params ReplaceTarget[] targets) =>
        await FindReplace.OnDiskAsync(targets, matcher, replacement, before ?? NothingBeforeFilesWrite.Instance, Token);

    [Fact]
    public async Task Replacing_on_disk_keeps_the_files_encoding_byte_order_mark_and_line_endings()
    {
        var path = _temp.Combine("Player.cpp");
        File.WriteAllBytes(path, [.. new UnicodeEncoding(false, true).GetPreamble(), .. Encoding.Unicode.GetBytes("void Dash()\r\n{\r\n    Dash();\r\n}\r\n")]);

        var result = Assert.Single(await ReplaceAsync(Matcher("Dash"), "Sprint", null, new ReplaceTarget(path, "Player.cpp")));

        Assert.Equal((2, null), (result.Replaced, result.Problem));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        Assert.Equal("void Sprint()\r\n{\r\n    Sprint();\r\n}\r\n", Encoding.Unicode.GetString(bytes[2..]));
    }

    [Fact]
    public void A_regular_expressions_groups_go_into_the_replacement()
    {
        var matcher = Matcher(@"Spend\((\w+)\)", regex: true);
        const string Text = "Spend(DashCost);\nSpend(JumpCost);\n";

        var plan = FindReplace.Plan(Text, new ReplaceTarget("p", "p"), matcher, "Pay($1, $$)");

        Assert.Equal("Pay(DashCost, $);\nPay(JumpCost, $);\n", FindReplace.Apply(Text, plan.Edits));
        // Without a regular expression, a dollar sign is just that.
        Assert.Equal("$1;\n", FindReplace.Apply("x;\n", FindReplace.Plan("x;\n", new ReplaceTarget("p", "p"), Matcher("x"), "$1").Edits));
    }

    [Fact]
    public async Task A_file_that_changed_since_it_was_searched_is_searched_again_before_its_replaced()
    {
        var path = _temp.Write("Player.cpp", "Dash();\nDash();\n");
        var matcher = Matcher("Dash");
        var found = FindInFiles.Search(path, "Player.cpp", File.ReadAllText(path), matcher, int.MaxValue, out _);
        // A line comes in above the matches, so neither is where it was.
        File.WriteAllText(path, "// new\nDash();\nDash();\n");

        var all = Assert.Single(await ReplaceAsync(matcher, "Sprint", null, new ReplaceTarget(path, "Player.cpp") { Found = found }));

        Assert.Equal(2, all.Replaced);
        Assert.Equal("// new\nSprint();\nSprint();\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task One_match_is_replaced_only_while_its_still_where_it_was()
    {
        var path = _temp.Write("Player.cpp", "Dash();\nDash();\n");
        var matcher = Matcher("Dash");
        var second = FindInFiles.Search(path, "Player.cpp", File.ReadAllText(path), matcher, int.MaxValue, out _)[1];

        var replaced = Assert.Single(await ReplaceAsync(matcher, "Sprint", null, new ReplaceTarget(path, "Player.cpp") { Only = second }));
        Assert.Equal(1, replaced.Replaced);
        Assert.Equal("Dash();\nSprint();\n", File.ReadAllText(path));

        File.WriteAllText(path, "// new\nDash();\nSprint();\n");
        var first = FindInFiles.Search(path, "Player.cpp", "Dash();\nSprint();\n", matcher, int.MaxValue, out _)[0];
        var moved = Assert.Single(await ReplaceAsync(matcher, "Sprint", null, new ReplaceTarget(path, "Player.cpp") { Only = first }));
        Assert.Equal(0, moved.Replaced);
        Assert.Contains("changed since it was searched", moved.Problem, StringComparison.Ordinal);
        Assert.Equal("// new\nDash();\nSprint();\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Matches_left_out_stay_unless_the_file_changed_when_none_of_it_is_replaced()
    {
        var path = _temp.Write("Player.cpp", "Dash(1);\nDash(2);\nDash(3);\n");
        var matcher = Matcher("Dash");
        var found = FindInFiles.Search(path, "Player.cpp", File.ReadAllText(path), matcher, int.MaxValue, out _);
        var target = new ReplaceTarget(path, "Player.cpp") { Found = found, Excluded = [found[1]] };

        Assert.Equal(2, Assert.Single(await ReplaceAsync(matcher, "Sprint", null, target)).Replaced);
        Assert.Equal("Sprint(1);\nDash(2);\nSprint(3);\n", File.ReadAllText(path));

        File.WriteAllText(path, "// new\nDash(1);\nDash(2);\n");
        var changed = Assert.Single(await ReplaceAsync(matcher, "Sprint", null, target));
        Assert.Equal(0, changed.Replaced);
        Assert.Contains("Search again", changed.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_that_cant_be_written_are_reported_with_why_and_the_rest_are_replaced()
    {
        var readOnly = _temp.Write("ReadOnly.h", "Dash\n");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var latin1 = _temp.Combine("Latin1.txt");
        File.WriteAllBytes(latin1, [.. Encoding.ASCII.GetBytes("Dash "), 0xE9, (byte)'\n']);
        var fine = _temp.Write("Fine.cpp", "Dash\n");

        var results = await ReplaceAsync(Matcher("Dash"), "Sprint", null,
            new ReplaceTarget(readOnly, "ReadOnly.h"), new ReplaceTarget(latin1, "Latin1.txt"), new ReplaceTarget(fine, "Fine.cpp"));

        Assert.Equal(["It's read-only.", "It isn't UTF-8 or UTF-16 text, so it can't be written back as it was.", null], results.Select(r => r.Problem));
        Assert.Equal("Dash\n", File.ReadAllText(readOnly));
        Assert.Equal("Sprint\n", File.ReadAllText(fine));
    }

    [Fact]
    public async Task Files_are_got_ready_all_at_once_before_any_is_written_and_one_refused_is_left_as_it_is()
    {
        var a = _temp.Write("a.txt", "Dash\n");
        var b = _temp.Write("b.txt", "Dash\n");
        var before = new RecordingBeforeWrite { Refuse = { [b] = "It's opened by someone else." } };

        var results = await ReplaceAsync(Matcher("Dash"), "Sprint", before, new ReplaceTarget(a, "a.txt"), new ReplaceTarget(b, "b.txt"));

        Assert.Equal([a, b], Assert.Single(before.Calls));
        Assert.Equal(new (int, string?)[] { (1, null), (0, "It's opened by someone else.") }, results.Select(r => (r.Replaced, r.Problem)));
        Assert.Equal("Sprint\n", File.ReadAllText(a));
        Assert.Equal("Dash\n", File.ReadAllText(b));
    }

    [Fact]
    public void Edits_for_an_open_files_text_replace_each_match_where_it_is()
    {
        const string Text = "a Dash\r\nDash b Dash\r\n";

        var plan = FindReplace.Plan(Text, new ReplaceTarget("p", "p"), Matcher("dash"), "Run");

        Assert.Equal([2, 8, 15], plan.Edits.Select(e => e.Offset));
        Assert.Equal("a Run\r\nRun b Run\r\n", FindReplace.Apply(Text, plan.Edits));
    }

    private sealed class RecordingBeforeWrite : IBeforeFilesWrite
    {
        public Dictionary<string, string> Refuse { get; } = new(FindInFiles.PathComparer);

        public List<string[]> Calls { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> PrepareAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            Calls.Add([.. paths]);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(Refuse);
        }
    }
}
