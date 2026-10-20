using System.Globalization;
using Claudette.Core.ProjectTools;
using Claudette.Core.ProjectTools.EditorLogs;
using Claudette.Core.ProjectTools.Godot;
using Claudette.Core.ProjectTools.Unity;
using Claudette.Core.ProjectTools.Unreal;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectTools;

/// <summary>
/// Editor logs (DESIGN.md §18, "Editor logs"): which of an engine's processes are a project's and where each writes its
/// log, how bad a line is, and which logs are being written. Every process is a made-up one, and every file a temporary one.
/// </summary>
public sealed class EditorLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 15, 1, 20, TimeSpan.Zero);

    private readonly TempFolder _temp = new("claudette-editorlogs");

    public void Dispose() => _temp.Dispose();

    private static SystemProcess Process(int pid, string name, string commandLine, DateTimeOffset? started = null) => new(pid, name, commandLine, started ?? Start);

    // ---- Command lines --------------------------------------------------------------------------------------------------

    [Fact]
    public void Command_line_options_are_read_quoted_or_not_ignoring_case()
    {
        const string line = """
            "C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealEditor.exe" "D:\Games\Night Owl\NightOwl.uproject" -ABSLOG="D:\Logs\Night Owl.log" -run=Cook -logFile out.txt
            """;

        Assert.Equal(@"C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealEditor.exe", CommandLineOptions.Words(line)[0]);
        Assert.Equal(@"D:\Logs\Night Owl.log", CommandLineOptions.ValueOf(line, "abslog"));
        Assert.Equal("Cook", CommandLineOptions.ValueOf(line, "run"));
        Assert.Null(CommandLineOptions.ValueOf(line, "log"));
        Assert.Equal("out.txt", CommandLineOptions.ValueAfter(line, "-logfile"));
        Assert.Null(CommandLineOptions.ValueAfter(line, "out.txt"));
        Assert.True(CommandLineOptions.Has(line, "-LOGFILE"));
        Assert.False(CommandLineOptions.Has(line, "-game"));
        Assert.Empty(CommandLineOptions.Words(null));
    }

    // ---- Unreal -------------------------------------------------------------------------------------------------------

    private const string UProject = @"D:\Games\NightOwl\NightOwl.uproject";
    private const string UnrealRoot = @"D:\Games\NightOwl";

    private static EditorLogWriter? Unreal(string name, string arguments) =>
        UnrealLogs.Writer(Process(1, name, $@"C:\UE\Engine\Binaries\Win64\{name}.exe {arguments}"), UProject, UnrealRoot, "NightOwl", ToolOS.Windows);

    [Fact]
    public void An_unreal_editor_with_the_project_writes_its_log_or_the_next_one_free()
    {
        var editor = Unreal("UnrealEditor", $"\"{UProject}\"")!;

        Assert.Equal("Unreal editor", editor.Role);
        Assert.Equal(@"D:\Games\NightOwl\Saved\Logs\NightOwl.log", editor.LogFiles[0]);
        Assert.Equal(@"D:\Games\NightOwl\Saved\Logs\NightOwl_2.log", editor.LogFiles[1]);
        Assert.Equal(9, editor.LogFiles.Count);

        // With either slash, ignoring case.
        Assert.NotNull(Unreal("UnrealEditor", "d:/games/nightowl/NIGHTOWL.uproject"));
        // Another project's, or none.
        Assert.Null(Unreal("UnrealEditor", @"D:\Games\Other\Other.uproject"));
        Assert.Null(UnrealLogs.Writer(new SystemProcess(1, "UnrealEditor", null), UProject, UnrealRoot, "NightOwl", ToolOS.Windows));
    }

    [Theory]
    [InlineData("UnrealEditor-Cmd", "-run=ResavePackagesCommandlet", "Unreal commandlet ResavePackages")]
    [InlineData("UnrealEditor-Cmd", "-run=cook -targetplatform=Windows", "Unreal commandlet cook")]
    [InlineData("UnrealEditor-Cmd", "-ExecCmds=\"Automation RunTests Project\"", "Unreal editor (command line)")]
    [InlineData("UnrealEditor", "-game -log", "Unreal game")]
    [InlineData("UnrealEditor", "-server", "Unreal server")]
    [InlineData("UE4Editor", "-skipcompile", "Unreal editor")]
    public void An_unreal_process_says_what_it_is(string name, string arguments, string role) =>
        Assert.Equal(role, Unreal(name, $"\"{UProject}\" {arguments}")!.Role);

    [Fact]
    public void An_unreal_log_named_on_the_command_line_is_the_one_followed()
    {
        Assert.Equal([@"D:\Logs\Owl.log"], Unreal("UnrealEditor", $@"""{UProject}"" -abslog=D:\Logs\Owl.log")!.LogFiles);
        Assert.Equal([@"D:\Games\NightOwl\Saved\Logs\Cook.log"], Unreal("UnrealEditor-Cmd", $"\"{UProject}\" LOG=Cook")!.LogFiles);
        Assert.Equal([@"D:\Games\NightOwl\Saved\Logs\Cook.txt"], Unreal("UnrealEditor-Cmd", $"\"{UProject}\" -log=Cook.txt")!.LogFiles);
        // -log alone opens a log window, and names no file.
        Assert.Equal(9, Unreal("UnrealEditor", $"\"{UProject}\" -log")!.LogFiles.Count);
    }

    [Theory]
    [InlineData("[2026.10.20-15.01.22:123][  0]LogBlueprint: Error: [Compiler] Missing node", LogLineLevel.Error)]
    [InlineData("LogTemp: Warning: Slow frame", LogLineLevel.Warning)]
    [InlineData("[2026.10.20-15.01.22:123][  0]LogWindows: Error: === Critical error: ===", LogLineLevel.Error)]
    [InlineData("Error: Unknown command line option", LogLineLevel.Error)]
    [InlineData(@"D:\Games\NightOwl\Source\Owl.cpp(42): error C2065: 'x': undeclared identifier", LogLineLevel.Error)]
    [InlineData("Source/Owl.cpp:42:7: warning: unused variable 'y'", LogLineLevel.Warning)]
    [InlineData("[2026.10.20-15.01.22:123][  0]LogInit: Display: Errors: none so far", LogLineLevel.Normal)]
    [InlineData("LogInit: Build: ++UE5+Release-5.4", LogLineLevel.Normal)]
    public void An_unreal_line_is_an_error_or_a_warning_by_its_verbosity(string line, LogLineLevel level) =>
        Assert.Equal(level, UnrealLogs.Classify(line));

    [Fact]
    public void An_unreal_crash_is_its_critical_error_or_an_assertion()
    {
        Assert.True(UnrealLogs.IsCrash("[2026.10.20-15.01.22:123][  0]LogWindows: Error: === Critical error: ==="));
        Assert.True(UnrealLogs.IsCrash("Fatal error: [File:D:/Owl/Source/Owl.cpp] [Line: 12]"));
        Assert.False(UnrealLogs.IsCrash("LogTemp: Error: Something went wrong"));
    }

    [Fact]
    public void An_unreal_log_says_when_it_opened_in_the_machines_time()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Owl Time", TimeSpan.FromHours(-4), "Owl Time", "Owl Time");

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 1, 22, TimeSpan.FromHours(-4)), UnrealLogs.OpenedAt("Log file open, 10/20/26 15:01:22", zone));
        Assert.Null(UnrealLogs.OpenedAt("LogInit: Display: Starting", zone));
        Assert.Null(UnrealLogs.OpenedAt(null, zone));
    }

    // ---- Unity --------------------------------------------------------------------------------------------------------

    private const string EditorLog = @"C:\Users\owl\AppData\Local\Unity\Editor\Editor.log";

    private static EditorLogWriter? Unity(string arguments, ToolOS os = ToolOS.Windows, string root = @"D:\Games\Owl") =>
        UnityLogs.Writer(Process(1, "Unity", $@"""C:\Program Files\Unity\Hub\Editor\2022.3.20f1\Editor\Unity.exe"" {arguments}"), root, EditorLog, os);

    [Fact]
    public void A_unity_editor_with_the_project_writes_the_machines_editor_log_or_its_own()
    {
        var editor = Unity(@"-projectPath ""D:\Games\Owl""")!;
        Assert.Equal("Unity editor", editor.Role);
        Assert.Equal([EditorLog], editor.LogFiles);

        Assert.NotNull(Unity(@"-projectpath d:/games/owl/"));
        Assert.Null(Unity(@"-projectPath D:\Games\Owl2"));
        Assert.Null(Unity(@"-projectpath /g/owl", ToolOS.Linux, "/g/Owl"));
        Assert.Null(Unity("-batchmode"));

        var batch = Unity(@"-batchmode -projectPath D:\Games\Owl -logFile Logs\import.log")!;
        Assert.Equal("Unity (batch mode)", batch.Role);
        Assert.Equal([@"D:\Games\Owl\Logs\import.log"], batch.LogFiles);
        Assert.Equal([@"D:\Logs\owl.log"], Unity(@"-projectPath D:\Games\Owl -logFile D:\Logs\owl.log")!.LogFiles);
        // To standard output, as Claudette's own jobs run it: no file.
        Assert.Null(Unity(@"-batchmode -projectPath D:\Games\Owl -runTests -logFile -"));
    }

    [Theory]
    [InlineData("Assets/Player.cs(12,5): error CS0103: The name 'x' does not exist", LogLineLevel.Error)]
    [InlineData("Assets/Player.cs(3,7): warning CS0168: The variable 'e' is declared but never used", LogLineLevel.Warning)]
    [InlineData("NullReferenceException: Object reference not set to an instance of an object", LogLineLevel.Error)]
    [InlineData("Refreshing native plugins compatible for Editor in 1.23 ms", LogLineLevel.Normal)]
    public void A_unity_line_is_an_error_or_a_warning(string line, LogLineLevel level) => Assert.Equal(level, UnityLogs.Classify(line));

    [Fact]
    public void A_unity_crash_is_its_crash_report()
    {
        Assert.True(UnityLogs.IsCrash("Crash!!!"));
        Assert.True(UnityLogs.IsCrash("=================================================================\tNative Crash Reporting"));
        Assert.False(UnityLogs.IsCrash("Crashing is not happening here"));
    }

    // ---- Godot --------------------------------------------------------------------------------------------------------

    private static readonly ProjectToolPaths Paths = new(@"C:\Users\owl", AppData: @"C:\Users\owl\AppData\Roaming");

    private static GodotProject Godot(string settings = "") =>
        GodotProject.Parse($"""
            config_version=5

            [application]

            config/name="Night: Owl"
            {settings}
            """, @"D:\Games\Owl\project.godot", @"D:\Games\Owl", hasCSharpFiles: false);

    [Fact]
    public void A_godot_projects_log_is_in_its_user_data_folder_unless_its_settings_move_it()
    {
        Assert.Equal(@"C:\Users\owl\AppData\Roaming\Godot\app_userdata\Night_ Owl\logs\godot.log", GodotLogs.LogFile(Godot(), Paths, ToolOS.Windows));
        var unix = new ProjectToolPaths("/home/owl");
        Assert.Equal("/home/owl/.local/share/godot/app_userdata/Night_ Owl/logs/godot.log", GodotLogs.LogFile(Godot(), unix, ToolOS.Linux));
        Assert.Equal("/home/owl/Library/Application Support/Godot/app_userdata/Night_ Owl/logs/godot.log", GodotLogs.LogFile(Godot(), unix, ToolOS.MacOS));

        var custom = Godot("""
            config/use_custom_user_dir=true
            config/custom_user_dir_name="OwlGame"
            """);
        Assert.Equal(@"C:\Users\owl\AppData\Roaming\OwlGame\logs\godot.log", GodotLogs.LogFile(custom, Paths, ToolOS.Windows));

        var moved = GodotProject.Parse("""
            [debug]

            file_logging/log_path="res://logs/owl.log"
            """, @"D:\Games\Owl\project.godot", @"D:\Games\Owl", hasCSharpFiles: false);
        Assert.Equal(@"D:\Games\Owl\logs\owl.log", GodotLogs.LogFile(moved, Paths, ToolOS.Windows));

        var off = GodotProject.Parse("""
            [debug]

            file_logging/enable_file_logging.pc=false
            """, @"D:\Games\Owl\project.godot", @"D:\Games\Owl", hasCSharpFiles: false);
        Assert.False(off.FileLogging);
        Assert.Null(GodotLogs.LogFile(off, Paths, ToolOS.Windows));
    }

    [Fact]
    public void A_godot_process_with_the_project_says_what_it_is_and_writes_the_projects_log_or_its_own()
    {
        var project = Godot();
        const string log = @"C:\Logs\godot.log";
        EditorLogWriter? Writer(string arguments) => GodotLogs.Writer(Process(1, "Godot_v4.3-stable_win64", $@"C:\Godot\Godot_v4.3-stable_win64.exe {arguments}"), project, log, ToolOS.Windows);

        Assert.Equal("Godot editor", Writer(@"--editor --path D:\Games\Owl")!.Role);
        Assert.Equal("Godot editor", Writer(@"-e --path ""D:/Games/Owl/""")!.Role);
        Assert.Equal("Godot game", Writer(@"--path D:\Games\Owl --remote-debug tcp://127.0.0.1:6007")!.Role);
        Assert.Equal("Godot (headless)", Writer(@"--headless --path D:\Games\Owl --quit")!.Role);
        Assert.Equal([log], Writer(@"--path D:\Games\Owl")!.LogFiles);
        Assert.Equal([@"D:\Logs\run.log"], Writer(@"--path D:\Games\Owl --log-file D:\Logs\run.log")!.LogFiles);
        Assert.Null(Writer(@"--path D:\Games\Other"));
        Assert.Null(Writer(@"--project-manager --path D:\Games\Owl"));
        Assert.Null(GodotLogs.Writer(Process(1, "godot", @"godot --path D:\Games\Owl"), project, null, ToolOS.Windows));
    }

    [Theory]
    [InlineData("ERROR: Condition \"p_node == nullptr\" is true.", LogLineLevel.Error)]
    [InlineData("SCRIPT ERROR: Invalid call. Nonexistent function 'jump'.", LogLineLevel.Error)]
    [InlineData("USER WARNING: Health is low", LogLineLevel.Warning)]
    [InlineData("WARNING: ObjectDB instances leaked at exit", LogLineLevel.Warning)]
    [InlineData("   at: get_node (scene/main/node.cpp:1364)", LogLineLevel.Normal)]
    public void A_godot_line_is_an_error_or_a_warning(string line, LogLineLevel level) => Assert.Equal(level, GodotLogs.Classify(line));

    [Fact]
    public void A_godot_crash_is_its_crash_handler()
    {
        Assert.True(GodotLogs.IsCrash("CrashHandlerException: Program crashed"));
        Assert.True(GodotLogs.IsCrash("handle_crash: Program crashed with signal 11"));
        Assert.False(GodotLogs.IsCrash("ERROR: Something"));
    }

    // ---- Which logs are being written ---------------------------------------------------------------------------------

    private FollowedProject UnrealProject(out string logs)
    {
        logs = _temp.Combine("Saved", "Logs");
        Directory.CreateDirectory(logs);
        var uproject = _temp.Combine("NightOwl.uproject");
        return new FollowedProject(uproject, _temp.Path, UnrealLogs.Source(new UnrealProject(uproject, "NightOwl", _temp.Path, null, "NightOwlEditor", true, [], []), ToolOSExtensions.Current));
    }

    private static void WriteLog(string path, DateTimeOffset opened) =>
        File.WriteAllText(path, $"\uFEFFLog file open, {opened.UtcDateTime.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture)}\nLogInit: Display: Starting\n");

    [Fact]
    public void Each_process_writes_the_log_opened_soonest_after_it_started_that_no_other_has()
    {
        var project = UnrealProject(out var logs);
        var uproject = project.ProjectPath;
        var tracker = new EditorLogTracker(TimeZoneInfo.Utc, ToolOSExtensions.Current);
        var main = Path.Combine(logs, "NightOwl.log");
        var second = Path.Combine(logs, "NightOwl_2.log");
        // A commandlet from yesterday left its log; the editor's opened a moment after it started.
        WriteLog(second, Start.AddDays(-1));
        WriteLog(main, Start.AddSeconds(2));
        var editor = Process(10, "UnrealEditor", $"UnrealEditor.exe \"{uproject}\"", Start);
        var commandlet = Process(20, "UnrealEditor-Cmd", $"UnrealEditor-Cmd.exe \"{uproject}\" -run=Cook", Start.AddMinutes(30));
        var other = Process(30, "UnrealEditor", "UnrealEditor.exe D:/Other/Other.uproject", Start);

        var live = Assert.Single(tracker.Update([project], [editor, other]));
        Assert.Equal(main, live.Path);
        Assert.Same(project, live.Project);
        Assert.Equal("Unreal editor", Assert.Single(live.Writers).Role);

        // A commandlet whose log isn't there yet is looked at again next time; yesterday's isn't its.
        Assert.Equal([main], tracker.Update([project], [editor, commandlet]).Select(l => l.Path));

        WriteLog(second, Start.AddMinutes(30).AddSeconds(1));
        var both = tracker.Update([project], [commandlet, editor]);
        Assert.Equal([main, second], both.Select(l => l.Path));
        Assert.Equal("Unreal commandlet Cook", Assert.Single(both[1].Writers).Role);

        // Each keeps its file while it runs, and one that's gone writes none.
        Assert.Equal([second], tracker.Update([project], [commandlet]).Select(l => l.Path));
        Assert.Empty(tracker.Update([project], []));
        Assert.Empty(tracker.Update([], [editor]));
    }

    [Fact]
    public void Without_a_start_time_to_go_by_a_process_takes_the_first_log_that_is_there()
    {
        var project = UnrealProject(out var logs);
        var tracker = new EditorLogTracker(TimeZoneInfo.Utc, ToolOSExtensions.Current);
        WriteLog(Path.Combine(logs, "NightOwl_2.log"), Start);
        var editor = new SystemProcess(10, "UnrealEditor", $"UnrealEditor.exe \"{project.ProjectPath}\"");

        Assert.Equal(Path.Combine(logs, "NightOwl_2.log"), Assert.Single(tracker.Update([project], [editor])).Path);
    }

    [Fact]
    public void A_log_written_by_two_processes_is_one_log()
    {
        var root = _temp.CreateFolder("owl");
        File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n");
        var project = GodotProject.Read(Path.Combine(root, "project.godot"));
        var followed = new FollowedProject(project.File, root, GodotLogs.Source(project, new ProjectToolPaths(_temp.Combine("home"), _temp.Combine("appdata")), ToolOSExtensions.Current));
        var tracker = new EditorLogTracker(TimeZoneInfo.Utc, ToolOSExtensions.Current);
        var editor = Process(1, "godot", $"godot --editor --path \"{root}\"");
        var game = Process(2, "godot", $"godot --path \"{root}\"", Start.AddMinutes(1));

        var live = Assert.Single(tracker.Update([followed], [editor, game]));

        Assert.Equal(["Godot editor", "Godot game"], live.Writers.Select(w => w.Role));
        Assert.EndsWith(Path.Combine("logs", "godot.log"), live.Path, StringComparison.Ordinal);
    }
}
