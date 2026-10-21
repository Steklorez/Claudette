using Claudette.Core.ProjectTools.EditorLogs;
using Claudette.Core.ProjectTools.Godot;
using Claudette.Core.ProjectTools.Unity;
using Claudette.Core.ProjectTools.Unreal;

namespace Claudette.Core.Tests.ProjectTools;

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
