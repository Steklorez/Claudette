using Deventry.Core.ProjectTools.EditorLogs;
using Deventry.Core.ProjectTools.Godot;
using Deventry.Core.ProjectTools.Unity;
using Deventry.Core.ProjectTools.Unreal;

namespace Deventry.Core.Tests.ProjectTools;

/// <summary>
/// Editor logs' lines as entries (DESIGN.md §18, "Editor logs", "Its lines"): which lines start one and which carry it
/// on, each entry's level and category, Unreal's columns and times, and what's counted. Every line is a made-up one.
/// </summary>
public sealed class EditorLogParserTests
{
    /// <summary>Four hours behind UTC, so a UTC time and the machine's are told apart.</summary>
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Owl Time", TimeSpan.FromHours(-4), "Owl Time", "Owl Time");

    private static (IReadOnlyList<LogLine> Lines, LogCounts Counts) Parse(EditorLogParser parser, params string[] lines)
    {
        foreach (var line in lines)
        {
            parser.Add(line);
        }
        return (parser.TakeLines(), parser.TakeCounts());
    }

    // ---- Unreal -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Unreal_lines_are_entries_with_their_category_and_verbosity()
    {
        var (lines, counts) = Parse(UnrealLogs.Parser(Zone),
            "Log file open, 10/20/26 15:01:20",
            "LogWindows: Failed to load 'aqProf.dll' (GetLastError=126)",
            "LogInit: Display: Running engine for game: NightOwl",
            "[2026.10.20-19.01.22:123][  0]LogConfig: Applying CVar settings",
            "[2026.10.20-19.01.22:140][  0]LogBlueprint: Error: [Compiler] Can't connect pins",
            "[2026.10.20-19.01.22:141][  1]LogStreaming: Warning: Failed to read file",
            "[2026.10.20-19.01.22:150][  2]LogNet: Verbose: Tick",
            "[2026.10.20-19.01.22:151][  2]LogNet: VeryVerbose: Tock",
            "[2026.10.20-19.01.22:160][  3]LogShaderCompilers: Display: Shader compile failed:",
            "  /Engine/Private/Owl.usf(12): the second line of the message",
            "[2026.10.20-19.01.22:170][  3]LogCore: Fatal: Assertion failed");

        Assert.Equal(
            [LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Display, LogVerbosity.Log, LogVerbosity.Error, LogVerbosity.Warning,
             LogVerbosity.Verbose, LogVerbosity.Verbose, LogVerbosity.Display, LogVerbosity.Display, LogVerbosity.Error],
            lines.Select(l => l.Verbosity));
        Assert.Equal(
            [null, "LogWindows", "LogInit", "LogConfig", "LogBlueprint", "LogStreaming", "LogNet", "LogNet", "LogShaderCompilers", "LogShaderCompilers", "LogCore"],
            lines.Select(l => l.Category));
        Assert.Equal([9], lines.Index().Where(l => l.Item.Continues).Select(l => l.Index));
        Assert.Same(lines[6].Category, lines[7].Category);

        Assert.Equal(2, counts.Errors);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(2, counts[LogVerbosity.Display]);
        Assert.Equal(3, counts[LogVerbosity.Log]);
        Assert.Equal(2, counts[LogVerbosity.Verbose]);
        Assert.Equal(2, counts.Categories["LogNet"]);
        Assert.Equal(1, counts.Categories["LogShaderCompilers"]);
        Assert.False(counts.Categories.ContainsKey("Log file open, 10/20/26 15:01:20"));
    }

    [Fact]
    public void An_unreal_line_says_where_its_columns_are_and_shows_its_utc_time_in_the_machines()
    {
        var (lines, _) = Parse(UnrealLogs.Parser(Zone),
            "Log file open, 10/20/26 15:01:20",
            "LogInit: Display: Starting",
            "[2026.10.20-19.01.22:140][  0]LogBlueprint: Error: Can't connect pins",
            "[2026.10.20-19.01.22:141][ 12]LogConfig: Applying CVar settings",
            "  the second line",
            "[2026.10.20-19.01.23:000][ 13]Warning: No category");

        Assert.Null(lines[0].Columns);
        Assert.Equal(new LogColumns(0, null, "LogInit".Length, "LogInit: ".Length, "Display".Length, "LogInit: Display: ".Length), lines[1].Columns);

        var error = lines[2].Columns!;
        var text = lines[2].Text;
        Assert.Equal("[2026.10.20-19.01.22:140][  0]", text[..error.PrefixLength]);
        Assert.Equal("15:01:22.140", error.Time);
        Assert.Equal("LogBlueprint", text.Substring(error.PrefixLength, error.CategoryLength));
        Assert.Equal("Error", text.Substring(error.TagStart, error.TagLength));
        Assert.Equal("Can't connect pins", text[error.MessageStart..]);

        var log = lines[3].Columns!;
        Assert.Equal(-1, log.TagStart);
        Assert.Equal("Applying CVar settings", lines[3].Text[log.MessageStart..]);
        Assert.True(lines[4].Columns!.IsCarriedOn);

        var bare = lines[5];
        Assert.Equal(LogVerbosity.Warning, bare.Verbosity);
        Assert.Null(bare.Category);
        Assert.Equal(0, bare.Columns!.CategoryLength);
        Assert.Equal("Warning", bare.Text.Substring(bare.Columns.TagStart, bare.Columns.TagLength));
    }

    [Fact]
    public void An_unreal_log_with_its_own_times_or_seconds_since_the_start_shows_them_as_they_are()
    {
        var (local, _) = Parse(UnrealLogs.Parser(Zone),
            "Log file open, 10/20/26 15:01:20",
            "[2026.10.20-15.01.22:140][  0]LogInit: Display: Local times");
        Assert.Equal("15:01:22.140", local[1].Columns!.Time);

        var (started, _) = Parse(UnrealLogs.Parser(Zone), "[0012.34][  0]LogInit: Display: Since the start");
        Assert.Equal("12.34s", started[0].Columns!.Time);

        // Without the first line to go by, UTC, as Unreal writes by default.
        var (utc, _) = Parse(UnrealLogs.Parser(Zone), "[2026.10.20-19.01.22:140][  0]LogInit: Display: Read from the middle");
        Assert.Equal("15:01:22.140", utc[0].Columns!.Time);
    }

    [Fact]
    public void An_error_within_an_unreal_message_is_an_entry_of_its_own()
    {
        var (lines, counts) = Parse(UnrealLogs.Parser(Zone),
            "[2026.10.20-19.01.22:140][  0]LogLiveCoding: Display: Compiling",
            @"D:\Games\NightOwl\Source\Owl.cpp(42): error C2065: 'x': undeclared identifier",
            "  and the line after it",
            "LogTemp: Warning: Without a time, but with a verbosity");

        Assert.Equal([LogVerbosity.Display, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Warning], lines.Select(l => l.Verbosity));
        Assert.Equal([false, false, true, false], lines.Select(l => l.Continues));
        Assert.Equal("LogLiveCoding", lines[1].Category);
        Assert.Equal("LogTemp", lines[3].Category);
        Assert.Equal(1, counts.Errors);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(1, counts[LogVerbosity.Display]);
    }

    [Fact]
    public void Lines_passed_over_are_counted_and_a_reset_starts_afresh()
    {
        var parser = UnrealLogs.Parser(Zone);
        parser.Add("[2026.10.20-19.01.22:140][  0]LogTemp: Error: Before the window", kept: false);
        parser.Add("[2026.10.20-19.01.22:141][  0]LogWindows: Error: === Critical error: ===");

        Assert.Single(parser.TakeLines());
        var counts = parser.TakeCounts();
        Assert.Equal(2, counts.Errors);
        Assert.True(counts.Crashed);
        Assert.Equal(0, parser.TakeCounts().Errors);

        parser.Add("[2026.10.20-19.01.22:142][  0]LogTemp: Display: Old");
        parser.Reset();
        Assert.Empty(parser.TakeLines());
        Assert.Equal(0, parser.TakeCounts()[LogVerbosity.Display]);
    }

    // ---- Unity --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_unity_scripts_message_is_what_its_stack_trace_says_and_its_trace_carries_it_on()
    {
        var (lines, counts) = Parse(UnityLogs.Parser(),
            "Refreshing native plugins compatible for Editor in 1.23 ms",
            "Something odd",
            "UnityEngine.Debug:LogWarning (object)",
            "Player:Start () (at Assets/Scripts/Player.cs:12)",
            "",
            "(Filename: Assets/Scripts/Player.cs Line: 12)",
            "",
            "NullReferenceException: Object reference not set to an instance of an object",
            "Player.Update () (at Assets/Scripts/Player.cs:20)",
            "Assets/Scripts/Player.cs(12,5): error CS0246: The type 'Foo' could not be found (are you missing a using directive?)",
            "Assets/Scripts/Player.cs(14,5): error CS0246: The type 'Bar' could not be found (are you missing a using directive?)",
            "Hello",
            "UnityEngine.Debug:Log(Object)");

        Assert.Equal(
            [LogVerbosity.Log, LogVerbosity.Warning, LogVerbosity.Warning, LogVerbosity.Warning, LogVerbosity.Warning, LogVerbosity.Warning, LogVerbosity.Warning,
             LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Log, LogVerbosity.Log],
            lines.Select(l => l.Verbosity));
        Assert.Equal([0, 1, 7, 9, 10, 11], lines.Index().Where(l => !l.Item.Continues).Select(l => l.Index));
        Assert.Equal([2, 3, 5, 8, 12], lines.Index().Where(l => l.Item.IsTrace).Select(l => l.Index));
        Assert.All(lines, l => Assert.Null(l.Category));
        Assert.Equal(3, counts.Errors);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(2, counts[LogVerbosity.Log]);
    }

    [Fact]
    public void A_unity_message_already_shown_keeps_its_level()
    {
        var parser = UnityLogs.Parser();
        parser.Add("Header");
        parser.Add("Something odd");
        Assert.Equal(2, parser.TakeLines().Count);

        parser.Add("UnityEngine.Debug:LogError (object)");
        Assert.Equal(LogVerbosity.Log, Assert.Single(parser.TakeLines()).Verbosity);
        var counts = parser.TakeCounts();
        Assert.Equal(0, counts.Errors);
        Assert.Equal(2, counts[LogVerbosity.Log]);
    }

    // ---- Unity's JSON log (issue #92) -----------------------------------------------------------------------------------

    [Fact]
    public void A_unity_JSON_line_is_one_entry_in_columns_with_its_message_and_stack_trace_under_it()
    {
        var (lines, counts) = Parse(UnityLogs.Parser(Zone),
            """{"timestamp":"2026-11-04T19:01:22.123Z","level":"Error","category":"Scripting","message":"Can't find Player\nIn the scene","stackTrace":["Player:Start () (at Assets/Player.cs:12)",""]}""",
            """{"time":"2026-11-04T19:01:23.000Z","Severity":"warning","msg":"Shader has no fallback"}""",
            """{"timestamp":"2026-11-04T19:01:24.000Z","logType":"Log","message":"Refresh done","stack":"Asset:Refresh ()\nEditor:Tick ()"}""",
            "A plain line of text, as Editor.log's",
            """{"no":"message"}""");

        Assert.Equal(
            ["[2026-11-04T19:01:22.123Z] Scripting: Error: Can't find Player", "In the scene", "Player:Start () (at Assets/Player.cs:12)",
             "[2026-11-04T19:01:23.000Z] Warning: Shader has no fallback",
             "[2026-11-04T19:01:24.000Z] Refresh done", "Asset:Refresh ()", "Editor:Tick ()",
             "A plain line of text, as Editor.log's", """{"no":"message"}"""],
            lines.Select(l => l.Text));
        Assert.Equal([0, 3, 4, 7, 8], lines.Index().Where(l => !l.Item.Continues).Select(l => l.Index));
        Assert.Equal([2, 5, 6], lines.Index().Where(l => l.Item.IsTrace).Select(l => l.Index));
        Assert.Equal([LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Warning, LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Log],
            lines.Select(l => l.Verbosity));
        Assert.Equal("Scripting", lines[0].Category);
        Assert.Null(lines[3].Category);

        // Its columns: the time in the machine's (four hours behind UTC), the category, the level, then the message.
        var first = lines[0].Columns!;
        Assert.Equal(("15:01:22.123", "[2026-11-04T19:01:22.123Z] ".Length, "Scripting".Length), (first.Time, first.PrefixLength, first.CategoryLength));
        Assert.Equal("Error", lines[0].Text.Substring(first.TagStart, first.TagLength));
        Assert.Equal("Can't find Player", lines[0].Text[first.MessageStart..]);
        Assert.True(lines[1].Columns!.IsCarriedOn);
        Assert.Equal(-1, lines[4].Columns!.TagStart);
        Assert.Null(lines[7].Columns);

        Assert.Equal((1, 1, 3), (counts.Errors, counts.Warnings, counts[LogVerbosity.Log]));
        Assert.Equal(1, counts.Categories["Scripting"]);
    }

    // ---- Unity's -timestamps (issue #122) -------------------------------------------------------------------------------

    [Fact]
    public void A_unity_JSON_messages_own_time_and_thread_are_the_thread_column_and_the_message_starts_after_them()
    {
        var (lines, _) = Parse(UnityLogs.Parser(Zone),
            """{"timestamp":"2026-11-05T13:14:55.764279000Z","level":"Log","message":"2026-11-05T13:14:55.764Z|0x9b98|Start HTTP server: port:7800"}""",
            """{"timestamp":"2026-11-05T13:14:56.000Z","level":"Error","category":"Scripting","message":"2026-11-05T13:14:56.000Z|0x9b98|Can't compile"}""",
            """{"timestamp":"2026-11-05T13:14:57.000Z","message":"2026-11-05T13:14:57.000Z|Only the time"}""",
            """{"timestamp":"2026-11-05T13:14:58.000Z","message":"No stamp"}""");

        // What's copied and found is the entry as it's written, Unity's stamp and all.
        Assert.Equal("[2026-11-05T13:14:55.764279000Z] 2026-11-05T13:14:55.764Z|0x9b98|Start HTTP server: port:7800", lines[0].Text);
        var first = lines[0].Columns!;
        // The time column is the entry's, in the machine's time; Unity's own is drawn as the thread.
        Assert.Equal("09:14:55.764", first.Time);
        Assert.Equal(("0x9b98", "2026-11-05T13:14:55.764Z|0x9b98|"), (first.Thread, lines[0].Text.Substring(first.ThreadStart, first.ThreadLength)));
        Assert.Equal("Start HTTP server: port:7800", lines[0].Text[first.MessageStart..]);
        // After the category and the level too.
        var error = lines[1].Columns!;
        Assert.Equal(("Error", "0x9b98", "Can't compile"), (lines[1].Text.Substring(error.TagStart, error.TagLength), error.Thread, lines[1].Text[error.MessageStart..]));
        Assert.Equal(error.TagStart + "Error: ".Length, error.ThreadStart);
        // Only the time: no thread, but the stamp still isn't the message's.
        Assert.Equal((null, "Only the time"), (lines[2].Columns!.Thread, lines[2].Text[lines[2].Columns!.MessageStart..]));
        Assert.Equal(-1, lines[3].Columns!.ThreadStart);
    }

    [Fact]
    public void A_unity_text_line_with_timestamps_shows_its_time_and_thread_in_columns_and_is_read_after_them()
    {
        var (lines, counts) = Parse(UnityLogs.Parser(Zone),
            "Unity Editor version:    6000.0.23f1",
            "2026-11-05T13:14:55.764Z|0x9b98|Refreshing native plugins compatible for Editor in 3.07 ms",
            "2026-11-05T13:14:56.000Z|0x9b98|NullReferenceException: Object reference not set to an instance of an object",
            "2026-11-05T13:14:56.001Z|0x9b98|Player.Update () (at Assets/Scripts/Player.cs:20)",
            "",
            "2026-11-05T13:14:57.000Z|0x1a2b3c|Assets/Scripts/Player.cs(12,5): error CS0246: The type 'Foo' could not be found");

        // Read after the stamp: the exception and the compiler's error are errors, the frame carries the exception on.
        Assert.Equal([LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error], lines.Select(l => l.Verbosity));
        Assert.Equal([0, 1, 2, 5], lines.Index().Where(l => !l.Item.Continues).Select(l => l.Index));
        Assert.True(lines[3].IsTrace);
        Assert.Equal(2, counts.Errors);

        // A line from before the stamps has no columns; one without a stamp after them is indented to the message.
        Assert.Null(lines[0].Columns);
        Assert.True(lines[4].Columns!.IsCarriedOn);
        var stamped = lines[1].Columns!;
        Assert.Equal(("09:14:55.764", "2026-11-05T13:14:55.764Z|".Length), (stamped.Time, stamped.PrefixLength));
        Assert.Equal(("0x9b98", "0x9b98|"), (stamped.Thread, lines[1].Text.Substring(stamped.ThreadStart, stamped.ThreadLength)));
        Assert.Equal("Refreshing native plugins compatible for Editor in 3.07 ms", lines[1].Text[stamped.MessageStart..]);
        Assert.Equal("0x1a2b3c", lines[5].Columns!.Thread);
    }

    // ---- Godot --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_godot_errors_indented_lines_carry_it_on()
    {
        var (lines, counts) = Parse(GodotLogs.Parser(),
            "Godot Engine v4.3.stable.official",
            "ERROR: Condition \"p_node == nullptr\" is true.",
            "   at: get_node (scene/main/node.cpp:1364)",
            "SCRIPT ERROR: Invalid call. Nonexistent function 'jump'.",
            "          at: _ready (res://player.gd:12)",
            "WARNING: ObjectDB instances leaked at exit",
            "Saved the scene");

        Assert.Equal(
            [LogVerbosity.Log, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Error, LogVerbosity.Warning, LogVerbosity.Log],
            lines.Select(l => l.Verbosity));
        Assert.Equal([2, 4], lines.Index().Where(l => l.Item.Continues && l.Item.IsTrace).Select(l => l.Index));
        Assert.Equal(2, counts.Errors);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(2, counts[LogVerbosity.Log]);
    }
}
