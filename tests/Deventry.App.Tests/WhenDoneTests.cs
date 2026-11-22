using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Power;
using Deventry.Core.TimedMessages;

namespace Deventry.App.Tests;

/// <summary>
/// <b>When Claude is done</b>, in the header cup's menu (DESIGN.md §9): shutting down or sleeping once nothing is left,
/// after a minute's countdown. The computer is a stand-in: nothing here shuts anything down or puts it to sleep.
/// </summary>
public class WhenDoneTests
{
    private const string BashRequest = """
        {"type":"control_request","request_id":"p1","request":{"subtype":"can_use_tool","tool_name":"Bash","tool_use_id":"t1","input":{"command":"npm test"}}}
        """;

    private static void ShutDown(TabTestHarness h) => h.OnUiThread(() => h.Services.WhenDone.ToggleShutDownCommand.Execute(null));

    private static void Sleep(TabTestHarness h) => h.OnUiThread(() => h.Services.WhenDone.ToggleSleepCommand.Execute(null));

    private static async Task<TabViewModel> WorkingTabAsync(TabTestHarness h)
    {
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "go";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "the turn");
        return tab;
    }

    [Fact]
    public async Task Off_until_the_menu_says_and_one_clears_the_other()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        await WorkingTabAsync(h);
        Assert.Equal(WhenDone.Nothing, whenDone.Choice);
        Assert.False(whenDone.ShowsBanner);

        ShutDown(h);
        Assert.True(whenDone.ShutsDown);
        Sleep(h);
        Assert.True(whenDone.Sleeps);
        Assert.False(whenDone.ShutsDown);
        // Sleep again: off.
        Sleep(h);
        Assert.Equal(WhenDone.Nothing, whenDone.Choice);
        Assert.Equal([WhenDone.ShutDown, WhenDone.Sleep], h.ComputerPower.Prepared);
    }

    [Fact]
    public async Task A_turn_holds_it_off_keeping_the_computer_awake_and_its_end_starts_the_countdown()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        await WorkingTabAsync(h);

        ShutDown(h);

        Assert.True(whenDone.IsWaiting);
        Assert.Equal("Shuts down once Claude is done:", whenDone.BannerTitle);
        Assert.Equal("waiting for 1 tab working.", whenDone.BannerText);
        // Keep this computer awake is off: this holds sleep off by itself until it's done, and the cup says why.
        Assert.False(h.Services.Settings.General.KeepAwake);
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Contains("Shuts down once Claude is done: waiting for 1 tab working.", h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() => whenDone.IsCountingDown, "the countdown");
        Assert.Equal("Shutting down in 60 seconds.", whenDone.BannerTitle);
        Assert.Equal("Claude is done.", whenDone.BannerText);
        Assert.Equal("Shut down now", whenDone.NowLabel);
        Assert.Contains(h.Notifier.Shown, n => n.Title == "Shutting down in a minute");
        h.Time.Advance(TimeSpan.FromSeconds(18));
        Assert.Equal("Shutting down in 42 seconds.", whenDone.BannerTitle);
    }

    [Fact]
    public async Task At_the_minute_Deventry_closes_to_shut_down_and_leaves_shutting_down_to_closing()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        var closes = 0;
        whenDone.CloseRequested += () => closes++;
        await h.OpenTabAsync();

        ShutDown(h);
        Assert.True(whenDone.IsCountingDown);
        h.Time.Advance(WhenDoneService.Countdown - TimeSpan.FromSeconds(1));
        Assert.Equal(0, closes);
        h.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, closes);
        Assert.True(whenDone.ShutsDownOnClose);
        Assert.False(whenDone.CanCancel);
        Assert.False(whenDone.ToggleSleepCommand.CanExecute(null));
        // The window's closing shuts the computer down, once it has written everything.
        Assert.Empty(h.ComputerPower.Runs);
    }

    [Fact]
    public async Task Sleep_sleeps_at_the_minute_with_nothing_holding_it_off_and_is_off_after()
    {
        // Kept awake whenever Deventry runs, as a remote client being on would keep it: that lets go for the sleep.
        await using var h = new TabTestHarness(s =>
        {
            s.General.KeepAwake = true;
            s.General.KeepAwakeWhen = Core.Settings.KeepAwakeWhen.DeventryRuns;
        });
        h.Services.SaveSettings();
        var whenDone = h.Services.WhenDone;
        var blockingWhenSlept = true;
        h.ComputerPower.OnRun = _ => blockingWhenSlept = h.SleepBlocker.IsBlocking;
        await h.OpenTabAsync();

        Sleep(h);
        Assert.Equal("Going to sleep in 60 seconds.", whenDone.BannerTitle);
        Assert.True(h.SleepBlocker.IsBlocking);
        h.Time.Advance(WhenDoneService.Countdown);
        // The clients have a moment to say so first.
        Assert.Empty(h.ComputerPower.Runs);
        h.Time.Advance(WhenDoneService.SayBeforeSleeping);

        await TabTestHarness.Eventually(() => h.ComputerPower.Runs.Count == 1, "the sleep");
        Assert.Equal(WhenDone.Sleep, h.ComputerPower.Runs[0]);
        Assert.False(blockingWhenSlept);
        Assert.Equal(WhenDone.Nothing, whenDone.Choice);
        Assert.False(whenDone.ShowsBanner);

        // Awake again, or it never slept: what held sleep off holds it again.
        h.Time.Advance(WhenDoneService.LetSleepFor);
        await TabTestHarness.Eventually(() => h.SleepBlocker.IsBlocking, "the hold to come back");
    }

    [Fact]
    public async Task Work_starting_in_the_minute_stops_the_countdown_until_it_is_done_again()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        var tab = await h.OpenTabAsync();
        ShutDown(h);
        Assert.True(whenDone.IsCountingDown);

        tab.ComposerText = "one more thing";
        await tab.SendCommand.ExecuteAsync(null);

        await TabTestHarness.Eventually(() => !whenDone.IsCountingDown, "the countdown to stop");
        Assert.True(whenDone.ShutsDown);
        Assert.NotEmpty(h.Notifier.Removed);
        h.Time.Advance(WhenDoneService.Countdown);
        Assert.False(whenDone.ShutsDownOnClose);
    }

    [Fact]
    public async Task A_tab_waiting_on_you_holds_it_off()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        var tab = await WorkingTabAsync(h);
        h.Transport.Emit(BashRequest);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.NeedsInput, "the prompt");

        Sleep(h);

        Assert.True(whenDone.IsWaiting);
        Assert.Equal("waiting for 1 tab waiting on you.", whenDone.BannerText);
    }

    [Fact]
    public async Task A_timed_message_within_the_hour_holds_a_shutdown_off_but_not_a_sleep_the_computer_wakes_from()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        await h.OpenTabAsync();
        h.OnUiThread(() => whenDone.SetTimedMessages(h.Time.GetUtcNow().AddMinutes(30), due: false, working: 0));

        ShutDown(h);
        Assert.True(whenDone.IsWaiting);
        Assert.StartsWith("waiting for a timed message at ", whenDone.BannerText, StringComparison.Ordinal);

        // Deventry wakes the computer for it, so sleeping now loses nothing.
        Sleep(h);
        Assert.True(whenDone.IsCountingDown);

        // Unless the computer may not wake: Windows' power plan, say.
        h.WakeTimer.Caveat = "Wake timers are off in this power plan.";
        h.OnUiThread(() => whenDone.CancelCommand.Execute(null));
        Sleep(h);
        Assert.True(whenDone.IsWaiting);

        // A message an hour and more away doesn't hold either.
        h.OnUiThread(() => whenDone.SetTimedMessages(h.Time.GetUtcNow().AddMinutes(61), due: false, working: 0));
        Assert.True(whenDone.IsCountingDown);
    }

    [Fact]
    public async Task A_timed_messages_turn_holds_it_off_too()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        h.WakeTimer.Setup = WakeSetup.NotNeeded;
        await h.OpenTabAsync();
        h.OnUiThread(() => whenDone.SetTimedMessages(h.Time.GetUtcNow().AddDays(1), due: false, working: 1));

        Sleep(h);

        Assert.Equal("waiting for a timed message's tab working.", whenDone.BannerText);
    }

    [Fact]
    public async Task Cancel_and_Shut_down_now_on_the_banner()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        var closes = 0;
        whenDone.CloseRequested += () => closes++;
        await h.OpenTabAsync();
        ShutDown(h);

        h.OnUiThread(() => whenDone.CancelCommand.Execute(null));
        Assert.Equal(WhenDone.Nothing, whenDone.Choice);
        Assert.False(h.SleepBlocker.IsBlocking);
        h.Time.Advance(WhenDoneService.Countdown);
        Assert.Equal(0, closes);

        ShutDown(h);
        h.OnUiThread(() => whenDone.GoNowCommand.Execute(null));
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task What_turning_it_on_asks_can_refuse_it_and_the_banner_says_why()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        h.ComputerPower.PrepareAnswer = "macOS doesn't let Deventry tell System Events to shut down.";

        ShutDown(h);

        Assert.Equal(WhenDone.Nothing, whenDone.Choice);
        Assert.True(whenDone.ShowsProblem);
        Assert.Equal("Can't shut down when Claude is done:", whenDone.BannerTitle);
        Assert.Equal("macOS doesn't let Deventry tell System Events to shut down.", whenDone.BannerText);
        h.OnUiThread(() => whenDone.DismissProblemCommand.Execute(null));
        Assert.False(whenDone.ShowsBanner);
    }

    [Fact]
    public async Task A_sleep_that_fails_says_why()
    {
        await using var h = new TabTestHarness();
        var whenDone = h.Services.WhenDone;
        h.ComputerPower.RunAnswer = "Windows didn't turn the display off (error 5).";
        await h.OpenTabAsync();
        Sleep(h);

        h.Time.Advance(WhenDoneService.Countdown);
        h.Time.Advance(WhenDoneService.SayBeforeSleeping);

        await TabTestHarness.Eventually(() => whenDone.ShowsProblem, "the problem");
        Assert.Equal("Couldn't put this computer to sleep:", whenDone.BannerTitle);
    }

    [Fact]
    public async Task One_this_computer_cant_do_is_greyed_out()
    {
        await using var h = new TabTestHarness();
        h.ComputerPower.Unavailable[WhenDone.Sleep] = "systemctl wasn't found.";

        Assert.False(h.Services.WhenDone.ToggleSleepCommand.CanExecute(null));
        Assert.True(h.Services.WhenDone.ToggleShutDownCommand.CanExecute(null));
    }
}
