using System.Text.Json.Nodes;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Settings;

namespace Claudette.App.Tests;

/// <summary>
/// Keeping the computer awake (DESIGN.md §9, "Keeping the computer awake"): Settings → General's switch, which the
/// header's cup turns on and off, only while Claude has work to finish or whenever Claudette runs.
/// </summary>
public class KeepAwakeTests
{
    private static TabTestHarness While(KeepAwakeWhen when, bool on = true) => new(s =>
    {
        s.General.KeepAwake = on;
        s.General.KeepAwakeWhen = when;
    });

    [Fact]
    public async Task Off_by_default_nothing_keeps_the_computer_awake()
    {
        Assert.False(new AppSettings().General.KeepAwake);
        Assert.Equal(KeepAwakeWhen.ClaudeHasWork, new AppSettings().General.KeepAwakeWhen);
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "go";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "the turn");

        var keepAwake = h.Services.KeepAwake;
        Assert.False(h.SleepBlocker.IsBlocking);
        Assert.False(keepAwake.IsKeepingAwake);
        Assert.Equal("This computer can sleep.\nClick to keep it awake while Claude has work to finish.\nRight-click to choose when.", keepAwake.Tooltip);
        Assert.Equal("Keep this computer awake", keepAwake.AccessibleName);
        Assert.StartsWith("Keep this computer awake is off.", keepAwake.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whenever_Claudette_runs_it_holds_from_the_start_and_the_cup_turns_it_off()
    {
        await using var h = While(KeepAwakeWhen.ClaudetteRuns);
        // Claudette starting with it on holds sleep off from the start, and lets go as it closes.
        var atStart = new FakeSleepBlocker();
        using (new KeepAwakeService(h.Services, atStart))
        {
            Assert.True(atStart.IsBlocking);
        }
        Assert.False(atStart.IsBlocking);
        Assert.True(atStart.IsDisposed);

        // The harness's own was made before the test's settings: saving them applies them, as a change in Settings does.
        h.Services.SaveSettings();
        var keepAwake = h.Services.KeepAwake;
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Equal(
            "Keeping this computer awake. The screen can still turn off.\nWhile Claudette runs, as Settings → General says.\nClick to let it sleep.\nRight-click to choose when.",
            keepAwake.Tooltip);
        Assert.Equal("Keep this computer awake: keeping it awake", keepAwake.AccessibleName);

        keepAwake.IsOn = false;

        Assert.False(h.SleepBlocker.IsBlocking);
        Assert.False(h.Services.Settings.General.KeepAwake);

        keepAwake.IsOn = true;
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Equal([true, false, true], h.SleepBlocker.Changes);
    }

    [Fact]
    public async Task Only_while_Claude_has_work_it_holds_through_a_turn_and_lets_go_a_little_after()
    {
        await using var h = While(KeepAwakeWhen.ClaudeHasWork);
        var tab = await h.OpenTabAsync();
        var keepAwake = h.Services.KeepAwake;
        Assert.False(h.SleepBlocker.IsBlocking);
        Assert.StartsWith("This computer can sleep: Claude has no work to finish.", keepAwake.Tooltip, StringComparison.Ordinal);

        tab.ComposerText = "go";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.SleepBlocker.IsBlocking, "the turn to keep it awake");
        Assert.Contains("While Claude has work to finish: 1 tab working.", keepAwake.Tooltip, StringComparison.Ordinal);

        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => !tab.IsWorking, "the turn to end");
        // The next turn often starts straight after: it holds on a little longer.
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Contains("Claude's work just finished", keepAwake.Tooltip, StringComparison.Ordinal);

        h.Time.Advance(KeepAwakeService.Grace - TimeSpan.FromSeconds(1));
        Assert.True(h.SleepBlocker.IsBlocking);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await TabTestHarness.Eventually(() => !h.SleepBlocker.IsBlocking, "it to let go");
        Assert.Equal([true, false], h.SleepBlocker.Changes);
    }

    [Fact]
    public async Task A_task_waiting_for_a_limit_to_reset_keeps_it_awake_until_it_wont_carry_on()
    {
        await using var h = While(KeepAwakeWhen.ClaudeHasWork);
        var tab = await h.OpenTabAsync();

        await AutoContinueTests.HitLimitAsync(h, tab, h.Time.GetUtcNow().AddHours(3));
        h.Time.Advance(TimeSpan.FromHours(1));

        Assert.True(tab.WillContinueAfterLimit);
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Contains("1 tab waiting for a usage limit to reset", h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        // Don't continue: nothing will start, so it can sleep once the grace is over.
        tab.DontContinueCommand.Execute(null);
        h.Time.Advance(KeepAwakeService.Grace);
        await TabTestHarness.Eventually(() => !h.SleepBlocker.IsBlocking, "it to let go");
    }

    [Fact]
    public async Task Work_running_in_the_background_after_the_turn_keeps_it_awake()
    {
        await using var h = While(KeepAwakeWhen.ClaudeHasWork);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Tool(null, "b1", "Bash", new JsonObject { ["command"] = "npm run dev", ["description"] = "Start the dev server", ["run_in_background"] = true }));
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "system", ["subtype"] = "task_started", ["task_id"] = "task-1", ["tool_use_id"] = "b1", ["task_type"] = "local_bash",
            ["description"] = "Start the dev server", ["is_backgrounded"] = true, ["session_id"] = "s1",
        }.ToJsonString());
        h.Transport.Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "ok", ["session_id"] = "s1" });
        await TabTestHarness.Eventually(() => tab.HasRunningTasks && !tab.IsWorking, "the task to outlive the turn");

        h.Time.Advance(KeepAwakeService.Grace * 2);

        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Contains("1 task running in the background", h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        h.Transport.Emit(Wire.Notification("task-1", "b1", "completed", "done"));
        await TabTestHarness.Eventually(() => !tab.HasRunningTasks, "the task to end");
        h.Time.Advance(KeepAwakeService.Grace);
        await TabTestHarness.Eventually(() => !h.SleepBlocker.IsBlocking, "it to let go");
    }

    [Fact]
    public async Task The_cups_menu_chooses_when_and_turns_it_on()
    {
        await using var h = new TabTestHarness();
        var keepAwake = h.Services.KeepAwake;

        keepAwake.ChooseCommand.Execute(KeepAwakeWhen.ClaudetteRuns);

        Assert.True(keepAwake.IsOn);
        Assert.True(keepAwake.WhileClaudetteRuns);
        Assert.False(keepAwake.WhileClaudeHasWork);
        Assert.True(h.SleepBlocker.IsBlocking);

        keepAwake.ChooseCommand.Execute(KeepAwakeWhen.ClaudeHasWork);
        Assert.True(keepAwake.WhileClaudeHasWork);
        Assert.False(h.SleepBlocker.IsBlocking);
        Assert.Equal("This computer can sleep: Claude has no work to finish.\nClick to let it sleep.\nRight-click to choose when.", keepAwake.Tooltip);
    }

    [Fact]
    public async Task Settings_General_has_the_same_switch_follows_the_cup_and_resets_to_off()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);
        var general = settings.General;
        var changed = new List<string?>();
        general.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        general.KeepAwake = true;
        general.KeepAwakeWhileClaudetteRuns = true;
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Null(general.KeepAwakeUnavailableText);

        // The cup turns it off while the window is open: the page follows.
        changed.Clear();
        h.Services.KeepAwake.IsOn = false;
        Assert.False(general.KeepAwake);
        Assert.Contains(nameof(general.KeepAwake), changed);

        general.KeepAwake = true;
        general.ResetAllSections();
        Assert.False(h.Services.Settings.General.KeepAwake);
        Assert.Equal(KeepAwakeWhen.ClaudeHasWork, h.Services.Settings.General.KeepAwakeWhen);
        Assert.False(h.SleepBlocker.IsBlocking);
        Assert.Contains(settings.SearchResultsFor("keep awake"), r => r is { Category: "General", Label: "Keep this computer awake" });
        Assert.Contains("Keep this computer awake is off.", settings.Advanced.KeepAwakeText, StringComparison.Ordinal);
    }
}
