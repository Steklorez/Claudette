using Claudette.Core.ProjectTools;

namespace Claudette.Core.Tests.ProjectTools;

/// <summary>Which parts of a run's log line are links (DESIGN.md §18, "Project tools", "A run's log").</summary>
public sealed class RunLogLinksTests
{
    /// <summary>Each link as the text it covers, its path and where in the file it points; a web link as its address.</summary>
    private static List<(string Text, string? Path, int? Line, int? Column)> Links(string line) =>
        [.. RunLogLinks.Find(line).Select(l => (line.Substring(l.Start, l.Length), l.File?.Path ?? l.Url?.ToString(), l.File?.Line, l.File?.Column))];

    [Theory]
    [InlineData("See https://dev.epicgames.com/documentation for help.", "https://dev.epicgames.com/documentation")]
    [InlineData("(docs: https://docs.unity3d.com/Manual/index.html)", "https://docs.unity3d.com/Manual/index.html")]
    [InlineData("http://127.0.0.1:8080/status, then", "http://127.0.0.1:8080/status")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar)", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("\"https://example.com/a?b=c\"", "https://example.com/a?b=c")]
    public void A_web_address_is_a_link_without_the_punctuation_after_it(string line, string address)
    {
        var link = Assert.Single(RunLogLinks.Find(line));

        Assert.Equal(address, line.Substring(link.Start, link.Length));
        Assert.Equal(new Uri(address), link.Url);
        Assert.Null(link.File);
    }

    [Fact]
    public void Other_schemes_arent_links()
    {
        Assert.Empty(RunLogLinks.Find("ftp://example.com/file and javascript:alert(1) and mailto:a@b.c"));
    }

    [Theory]
    // MSVC, Unreal and Unity: path(line) and path(line,column), drives with spaces and parentheses.
    [InlineData(@"D:\Game\Source\Game\Player.cpp(42): error C2065: 'Speed': undeclared identifier", @"D:\Game\Source\Game\Player.cpp(42)", @"D:\Game\Source\Game\Player.cpp", 42, null)]
    [InlineData(@"  C:\My Game\Source\Player.cpp(42,7): warning C4996", @"C:\My Game\Source\Player.cpp(42,7)", @"C:\My Game\Source\Player.cpp", 42, 7)]
    [InlineData(@"C:\Program Files (x86)\Microsoft Visual Studio\VC\include\xstring(123): note: see", @"C:\Program Files (x86)\Microsoft Visual Studio\VC\include\xstring(123)", @"C:\Program Files (x86)\Microsoft Visual Studio\VC\include\xstring", 123, null)]
    [InlineData("1>Assets/Scripts/Player.cs(12,5): error CS0103: The name 'speed' does not exist", "Assets/Scripts/Player.cs(12,5)", "Assets/Scripts/Player.cs", 12, 5)]
    [InlineData("Player.cpp(42,7-12): error", "Player.cpp(42,7-12)", "Player.cpp", 42, 7)]
    // gcc, clang, rustc and others: path:line:column.
    [InlineData("src/main.rs:42:7: error[E0425]: cannot find value", "src/main.rs:42:7", "src/main.rs", 42, 7)]
    [InlineData("/usr/include/stdio.h:33: note", "/usr/include/stdio.h:33", "/usr/include/stdio.h", 33, null)]
    [InlineData(@"C:\My Game\Source\Player.cpp:42:7: error: expected ';'", @"C:\My Game\Source\Player.cpp:42:7", @"C:\My Game\Source\Player.cpp", 42, 7)]
    [InlineData("In file included from ./include/game.h:3,", "./include/game.h:3", "./include/game.h", 3, null)]
    public void A_path_with_a_line_number_is_a_link_with_its_line(string line, string text, string path, int? number, int? column)
    {
        Assert.Equal([(text, path, number, column)], Links(line));
    }

    [Fact]
    public void Pythons_and_dotnets_tracebacks_link_the_path_and_say_the_line()
    {
        Assert.Equal([("tools/build.py", "tools/build.py", 42, null)], Links("  File \"tools/build.py\", line 42, in main"));
        Assert.Equal([(@"C:\Work\Tools\Pack.cs", @"C:\Work\Tools\Pack.cs", 17, null)], Links(@"   at Tools.Pack.Run() in C:\Work\Tools\Pack.cs:line 17"));
        Assert.Empty(Links("  File \"<frozen importlib._bootstrap>\", line 1"));
    }

    [Theory]
    [InlineData("Log file: Saved/Logs/Game.log.", "Saved/Logs/Game.log")]
    [InlineData(@"Writing C:\Work\Game\Binaries\Win64", @"C:\Work\Game\Binaries\Win64")]
    [InlineData("Wrote D:/Builds/Game/Game.exe", "D:/Builds/Game/Game.exe")]
    [InlineData("Output in ~/builds/game, done", "~/builds/game")]
    [InlineData("(see ../shared/notes.txt)", "../shared/notes.txt")]
    [InlineData("Cleaning Intermediate/", "Intermediate/")]
    [InlineData("Copying to /tmp/out", "/tmp/out")]
    public void A_path_with_a_folder_in_it_is_a_link(string line, string path)
    {
        Assert.Equal([(path, path, null, null)], Links(line));
    }

    [Fact]
    public void A_path_in_quotes_may_have_spaces()
    {
        Assert.Equal([(@"C:\Program Files\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat", @"C:\Program Files\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat", null, null)],
            Links(@"Running ""C:\Program Files\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat"" StarfallEditor Win64"));
        Assert.Equal([("src/my notes.txt", "src/my notes.txt", null, null)], Links("Can't open 'src/my notes.txt' for writing"));
    }

    [Theory]
    [InlineData("Building 3/120 actions")]
    [InlineData("[2026.10.17-12.34.56:789][  0]LogInit: Display: Running engine")]
    [InlineData("Took 12.5s at 127.0.0.1:8080")]
    [InlineData("Player.cpp is up to date")]
    [InlineData("and / or")]
    [InlineData("\\\\server\\share\\builds\\Game.exe")]
    [InlineData("//server/share/builds/Game.exe")]
    [InlineData("Result: Succeeded")]
    [InlineData("")]
    public void Text_that_cant_be_a_path_isnt_a_link(string line)
    {
        Assert.Empty(RunLogLinks.Find(line));
    }

    [Fact]
    public void Several_links_in_a_line_come_in_order_without_overlapping()
    {
        var line = @"D:\Game\Source\Player.h(12): note: see Source/Game/Player.cpp:40 and https://example.com/help";

        Assert.Equal(
        [
            (@"D:\Game\Source\Player.h(12)", @"D:\Game\Source\Player.h", 12, null),
            ("Source/Game/Player.cpp:40", "Source/Game/Player.cpp", 40, null),
            ("https://example.com/help", "https://example.com/help", null, null),
        ], Links(line));
    }

    [Fact]
    public void A_web_address_isnt_taken_for_a_path()
    {
        Assert.Equal([("https://example.com/src/main.rs:42", "https://example.com/src/main.rs:42", null, null)], Links("https://example.com/src/main.rs:42"));
    }

    [Fact]
    public void Only_the_start_of_a_very_long_line_is_looked_through()
    {
        var line = new string('x', RunLogLinks.MaxLookedAt) + " Saved/Logs/Game.log";

        Assert.Empty(RunLogLinks.Find(line));
        Assert.Single(RunLogLinks.Find("Saved/Logs/Game.log " + line));
    }
}
