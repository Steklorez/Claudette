using System.Globalization;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.ProjectTools;

namespace Claudette.App.Tests;

/// <summary>
/// Editor logs (DESIGN.md §18, "Editor logs"): while a project's editor runs, its log is a row under one of the project's
/// tabs, read as it's written, and it stays after the editor ends. The engine and project are a few files in a temporary
/// folder, the editor a made-up process, and its log a file the test writes.
/// </summary>
public class EditorLogsTests
{
    private sealed record Fixture(TabTestHarness H, FakeLauncher Launcher, FakeSystemProcesses Processes, string UProject, string Logs) : IAsyncDisposable
    {
        public string MainLog => Path.Combine(Logs, "NightOwl.log");

        public ValueTask DisposeAsync() => H.DisposeAsync();

        /// <summary>Lets the clock run long enough for a look at the processes, and the reads after it.</summary>
        public void Look() => H.Time.Advance(TimeSpan.FromSeconds(EditorLogService.ReadsPerCheck));

        /// <summary>Starts an Unreal process with the project on its command line.</summary>
        public SystemProcess StartEditor(int pid, string name = "UnrealEditor", string arguments = "")
        {
            var process = new SystemProcess(pid, name, $"C:\\UE\\Engine\\Binaries\\Win64\\{name}.exe \"{UProject}\" {arguments}", H.Time.GetUtcNow());
            Processes.Start(process);
            return process;
        }

        /// <summary>A new log, as Unreal starts one: the line saying when it opened, then <paramref name="lines"/>.</summary>
        public void WriteLog(string path, params string[] lines)
        {
            Directory.CreateDirectory(Logs);
            var opened = H.Time.GetUtcNow().AddSeconds(1).UtcDateTime.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllText(path, string.Join("\n", [$"Log file open, {opened}", .. lines]) + "\n");
        }

        public static void AppendLog(string path, params string[] lines) => File.AppendAllText(path, string.Join("\n", lines) + "\n");
    }

    private static async Task<(Fixture F, TabViewModel Tab)> OpenAsync(Action<Core.Settings.AppSettings>? configure = null)
    {
        var (h, launcher, uproject) = ProjectToolsTests.UnrealHarness(configure);
        var processes = new FakeSystemProcesses();
        h.Services.ProjectTools.Processes = processes;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        return (new Fixture(h, launcher, processes, uproject, Path.Combine(h.WorkFolder, "Saved", "Logs")), tab);
    }

    private static IReadOnlyList<EditorLogViewModel> Logs(TabViewModel tab) => InlineDispatcher.Read(() => tab.ProjectTools.Logs.ToArray());

    [Fact]
    public async Task A_running_editor_gets_a_row_under_the_tab_with_its_log_which_stays_after_it_closes()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting", "[2026.09.28-12.00.01:000][  0]LogBlueprint: Error: Missing node", "LogTemp: Warning: Slow");
        f.StartEditor(4412);
        Assert.Empty(Logs(tab));

        f.Look();

        var log = Assert.Single(Logs(tab));
        Assert.Equal("NightOwl.log", log.FileName);
        Assert.Equal(f.MainLog, log.LogPath);
        Assert.True(log.IsRunning);
        Assert.Equal("Unreal editor", log.Role);
        Assert.Contains("LogTemp: Warning: Slow", log.Output);
        Assert.Equal(1, log.Errors);
        Assert.Equal(1, log.Warnings);
        Assert.Equal("Unreal editor · 1 error · 1 warning", log.Detail);
        Assert.Equal($"Unreal editor running since {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)} (PID 4412).", log.Status);
        Assert.Null(log.Timing);
        // Its log can be shown, but appearing doesn't take the main area; the tab has a row under it now.
        Assert.Contains(log, h.Shell.OpenTabs);
        Assert.Same(tab, h.Shell.Selected);
        Assert.True(tab.HasRowsUnder);
        Assert.True(tab.ShowsLogs);
        Assert.Equal("1 log", tab.RowsUnderText);

        // New lines come as they're written, each read once.
        Fixture.AppendLog(f.MainLog, "LogTemp: Display: Loaded");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("LogTemp: Display: Loaded", log.Output[^1]);
        Assert.Single(log.Output, l => l == "LogTemp: Display: Loaded");

        // Shown, in the main area: its errors are seen.
        Assert.Equal(1, log.UnseenErrors);
        log.OpenCommand.Execute(null);
        Assert.Same(log, h.Shell.Selected);
        Assert.True(log.IsShown);
        Assert.Equal(0, log.UnseenErrors);
        Assert.Same(tab, h.Shell.ProjectTab);

        // The editor closes: the row stays, saying so, with its lines.
        Fixture.AppendLog(f.MainLog, "Log file closed, 09/28/26 12:00:09");
        f.Processes.End(4412);
        f.Look();
        Assert.False(log.IsRunning);
        Assert.Same(log, Assert.Single(Logs(tab)));
        Assert.Equal("Log file closed, 09/28/26 12:00:09", log.Output[^1]);
        Assert.Equal($"Closed · {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)} · 1 error · 1 warning", log.Detail);
        Assert.Equal($"Unreal editor closed at {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)}.", log.Status);
        Assert.NotNull(log.Timing);
    }

    [Fact]
    public async Task An_editor_starting_again_brings_the_same_row_back_with_its_new_log()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogTemp: Error: First session");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        f.Processes.End(4412);
        f.Look();
        Assert.False(log.IsRunning);

        // As Unreal starts again: the old log becomes a backup, and a new one begins.
        File.Move(f.MainLog, Path.Combine(f.Logs, "NightOwl-backup-2026.09.28-12.00.05.log"));
        f.H.Time.Advance(TimeSpan.FromMinutes(5));
        f.StartEditor(5120);
        f.WriteLog(f.MainLog, "LogInit: Display: Second session");
        f.Look();

        Assert.Same(log, Assert.Single(Logs(tab)));
        Assert.True(log.IsRunning);
        Assert.Contains("LogInit: Display: Second session", log.Output);
        Assert.DoesNotContain("LogTemp: Error: First session", log.Output);
        Assert.Equal(0, log.Errors);
        Assert.Contains("(PID 5120)", log.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_the_row_while_the_editor_runs_keeps_it_away_until_the_editor_starts_again()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        log.CloseCommand.Execute(null);

        Assert.Empty(Logs(tab));
        Assert.DoesNotContain(log, f.H.Shell.OpenTabs);
        // Its tab takes its place.
        Assert.Same(tab, f.H.Shell.Selected);
        Fixture.AppendLog(f.MainLog, "LogTemp: Display: More");
        f.Look();
        Assert.Empty(Logs(tab));

        // The editor ends and starts again: the row is back, with the new log.
        f.Processes.End(4412);
        f.Look();
        f.StartEditor(5120);
        File.Delete(f.MainLog);
        f.WriteLog(f.MainLog, "LogInit: Display: Again");
        f.Look();
        var again = Assert.Single(Logs(tab));
        Assert.Contains("LogInit: Display: Again", again.Output);

        // Closed once the editor has ended, it's gone; the next start is a log of its own.
        f.Processes.End(5120);
        f.Look();
        again.CloseCommand.Execute(null);
        Assert.Empty(Logs(tab));
        f.StartEditor(6000);
        File.Delete(f.MainLog);
        f.WriteLog(f.MainLog, "LogInit: Display: Third");
        f.Look();
        Assert.NotSame(again, Assert.Single(Logs(tab)));
    }

    [Fact]
    public async Task Ctrl_W_on_an_editors_log_closes_it()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        await f.H.Shell.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Empty(Logs(tab));
        Assert.Same(tab, f.H.Shell.Selected);
        Assert.Contains(tab, f.H.Shell.AllTabs);
    }

    [Fact]
    public async Task A_commandlet_beside_the_editor_gets_a_row_for_the_log_it_writes()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        f.H.Time.Advance(TimeSpan.FromMinutes(1));

        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=ResavePackages");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"), "LogResave: Display: Resaving");
        f.Look();

        Assert.Equal(["NightOwl.log", "NightOwl_2.log"], Logs(tab).Select(l => l.FileName));
        Assert.Equal("Unreal commandlet ResavePackages", Logs(tab)[1].Role);
        Assert.Equal("2 logs", tab.RowsUnderText);
    }

    /// <summary>
    /// Another tab of the project in its group, at <paramref name="index"/>: made as a restored one is, so it starts no
    /// session until it's selected, and its project found.
    /// </summary>
    private static async Task<TabViewModel> AddTabAsync(Fixture f, int index)
    {
        var h = f.H;
        var tab = InlineDispatcher.Read(() =>
        {
            var other = new TabViewModel(h.Services, h.Shell, new Core.Settings.TabState { Folder = h.WorkFolder }, isRestored: true);
            h.Shell.Groups.Single().Tabs.Insert(index, other);
            h.Shell.OpenTabs.Add(other);
            return other;
        });
        await tab.ProjectTools.RefreshAsync();
        return tab;
    }

    [Fact]
    public async Task The_log_goes_under_the_tab_that_launched_the_editor_and_moves_to_another_when_that_tab_closes()
    {
        var (f, first) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        var second = await AddTabAsync(f, 1);

        await second.ProjectTools.RunActionCommand.ExecuteAsync(InlineDispatcher.Read(() => second.ProjectTools.Actions.Single(a => a.Id == "launch-editor")));
        Assert.NotEmpty(f.Launcher.Started);
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();

        Assert.Empty(Logs(first));
        var log = Assert.Single(Logs(second));

        await h.Shell.CloseTabCommand.ExecuteAsync(second);

        // It keeps its lines under the other tab of the project.
        Assert.Same(log, Assert.Single(Logs(first)));
        Assert.Contains("LogInit: Display: Starting", log.Output);
        Assert.Contains(log, h.Shell.OpenTabs);

        await h.Shell.CloseTabCommand.ExecuteAsync(first);
        Assert.DoesNotContain(log, h.Shell.OpenTabs);
    }

    [Fact]
    public async Task A_commandlet_a_tab_started_goes_under_that_tab_and_one_nobody_started_under_the_first()
    {
        var (f, started) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        // A tab of the project above it in the sidebar, which would have the log otherwise.
        var above = await AddTabAsync(f, 0);
        // Claude ran it in the tab that opened first, so it's among that tab's processes.
        h.Trees.Trees[h.Transport.ProcessId!.Value].Children.Add((4500, "UnrealEditor-Cmd"));

        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=Cook");
        f.WriteLog(f.MainLog);
        f.Look();

        Assert.Empty(Logs(above));
        Assert.Single(Logs(started));

        f.StartEditor(4600, "UnrealEditor", "-game");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"));
        f.Look();
        Assert.Equal("NightOwl_2.log", Assert.Single(Logs(above)).FileName);
    }

    [Fact]
    public async Task Collapsed_the_tab_folds_its_logs_away_and_its_badge_shows_errors_not_seen_and_a_crash()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        Assert.False(log.IsError);

        tab.ToggleRowsCollapsedCommand.Execute(null);
        Assert.True(tab.AreRowsCollapsed);
        Assert.False(tab.ShowsLogs);
        Assert.Equal(1, tab.CollapsedRowCount);
        Assert.False(tab.CollapsedIsError);

        Fixture.AppendLog(f.MainLog, "LogBlueprint: Error: Missing node");
        f.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(tab.CollapsedIsError);
        Assert.Contains("NightOwl.log · Unreal editor · 1 error", tab.CollapsedTip, StringComparison.Ordinal);

        // Looked at, the errors are seen; the row comes out from under the fold.
        log.OpenCommand.Execute(null);
        Assert.False(tab.AreRowsCollapsed);
        Assert.False(log.IsError);

        Fixture.AppendLog(f.MainLog, "LogWindows: Error: === Critical error: ===");
        f.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(log.IsCrashed);
        Assert.True(log.IsError);
        Assert.StartsWith("Crashed", log.Detail, StringComparison.Ordinal);
        Assert.StartsWith("Unreal editor crashed", log.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_setting_off_there_are_no_logs_and_turning_it_off_takes_them_away()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        f.H.Services.Settings.ProjectTools.ShowEditorLogs = false;
        f.H.Services.SaveSettings();

        Assert.Empty(Logs(tab));
        Assert.DoesNotContain(log, f.H.Shell.OpenTabs);
        Assert.Same(tab, f.H.Shell.Selected);
        f.Look();
        Assert.Empty(Logs(tab));

        f.H.Services.Settings.ProjectTools.ShowEditorLogs = true;
        f.H.Services.SaveSettings();
        f.Look();
        Assert.Single(Logs(tab));
    }

    [Fact]
    public async Task Another_projects_editor_gets_no_row()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.Processes.Start(new SystemProcess(4412, "UnrealEditor", "UnrealEditor.exe D:/Other/Other.uproject", f.H.Time.GetUtcNow()));
        f.WriteLog(f.MainLog);

        f.Look();

        Assert.Empty(Logs(tab));
        Assert.False(tab.HasRowsUnder);
    }
}
