using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Development;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.App.Tests;

/// <summary>The card over the window while Deventry closes or restarts (DESIGN.md §13, "What closing does, on screen").</summary>
public class ClosingViewModelTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly InlineDispatcher _dispatcher = new();

    private ClosingViewModel Closing(int steps = 4, ClosingJoulene? joulene = null, TimeSpan? showAfter = null) =>
        new(_time, _dispatcher, "Closing Deventry…", steps, joulene, showAfter ?? ClosingViewModel.ShowAfter);

    [Fact]
    public void It_shows_only_once_closing_has_taken_a_moment()
    {
        using var closing = Closing();

        _time.Advance(ClosingViewModel.ShowAfter - TimeSpan.FromMilliseconds(1));
        Assert.False(closing.IsShown);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(closing.IsShown);
    }

    [Fact]
    public void A_restarts_card_shows_at_once()
    {
        using var closing = Closing(showAfter: TimeSpan.Zero);

        Assert.True(closing.IsShown);
    }

    [Fact]
    public void Each_step_says_what_it_is_and_fills_the_bar_without_going_past_its_end()
    {
        using var closing = Closing(steps: 5);
        Assert.Equal((0, 5, "0 of 5 steps done"), (closing.Done, closing.Total, closing.Progress));

        closing.TellingBots(["Telegram", "Discord"]);
        Assert.Equal("Telling Telegram and Discord that Deventry is closing", closing.Step);
        closing.Advance();

        closing.StoppingTabs(["Grappling hook", "Stamina", "Inventory"]);
        Assert.Equal("Stopping Claude in 3 tabs", closing.Step);
        closing.TabStopped();
        Assert.Equal("Stopping Claude in 3 tabs (1 of 3 done)", closing.Step);
        closing.TabStopped();
        closing.TabStopped();
        Assert.Equal(("Stopping Claude in 3 tabs (3 of 3 done)", 4), (closing.Step, closing.Done));
        Assert.Equal([4L, 1L], closing.Bar.Select(s => s.Tokens));

        closing.Saving(restart: false);
        Assert.Equal("Saving your tabs, drafts and scratch pads", closing.Step);
        closing.Advance();
        closing.Advance();
        Assert.Equal(5, closing.Done);
    }

    [Fact]
    public void One_tab_is_named_and_a_restart_says_what_it_waits_for()
    {
        using var closing = Closing(steps: 3, showAfter: TimeSpan.Zero);

        closing.StoppingTabs(["Grappling hook"]);
        Assert.Equal("Stopping Claude in Grappling hook", closing.Step);
        closing.TabStopped();
        Assert.Equal("Stopping Claude in Grappling hook", closing.Step);

        closing.Waiting("Starting the new build");
        Assert.Equal("Starting the new build", closing.Step);
        closing.Saving(restart: true);
        Assert.Equal("Finishing up", closing.Step);
    }

    [Fact]
    public void Joulene_waves_goodbye_once_the_card_shows_in_her_hat()
    {
        using var closing = Closing(joulene: new ClosingJoulene(Still: false, Hat: "santa", PixelsPerCell: 4));
        Assert.Equal(("wave", "santa", 4.0), (closing.Frame?.Pose, closing.Frame?.Hat, closing.PixelsPerCell));

        _time.Advance(ClosingViewModel.ShowAfter);
        _time.Advance(ClosingViewModel.WaveStep);
        Assert.Equal("waveHigh", closing.Frame?.Pose);
        _time.Advance(ClosingViewModel.WaveStep);
        Assert.Equal("wave", closing.Frame?.Pose);
    }

    [Fact]
    public void With_motion_reduced_she_stands_still_and_turned_off_she_isnt_there()
    {
        using (var still = Closing(joulene: new ClosingJoulene(Still: true, Hat: null, PixelsPerCell: 3)))
        {
            _time.Advance(ClosingViewModel.ShowAfter + (ClosingViewModel.WaveStep * 3));
            Assert.Equal("wave", still.Frame?.Pose);
        }

        using var off = Closing(joulene: null);
        Assert.Null(off.Frame);
    }

    [Fact]
    public async Task A_restart_shows_its_card_at_once_closing_carries_it_on_and_a_failed_one_takes_it_away()
    {
        await using var h = new TabTestHarness();
        var main = new MainWindowViewModel(h.Services);
        IRestartHost host = main;

        await host.CloseTabsAsync("Restarting into the new build…", "Starting the new build");
        var closing = main.Closing!;
        Assert.Equal(("Restarting into the new build…", "Starting the new build", true), (closing.Title, closing.Step, closing.IsShown));
        // The wait, then saving: no tabs or bots here.
        Assert.Equal((0, 2), (closing.Done, closing.Total));

        host.Exit();
        Assert.Equal(1, closing.Done);
        // The window closing goes on with the restart's card.
        Assert.Same(closing, main.BeginClosing());

        host.Recover(new RestartSnapshot());
        Assert.Null(main.Closing);
        using var again = main.BeginClosing();
        Assert.Equal("Closing Deventry…", again.Title);
    }

    [Fact]
    public async Task Closing_the_tabs_counts_each_as_it_stops()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        await h.OpenTabAsync();
        using var closing = new ClosingViewModel(h.Time, h.Services.Dispatcher, "Closing Deventry…", 3, null, ClosingViewModel.ShowAfter);

        await InlineDispatcher.RunAsync(() => h.Shell.ShutDownAsync(closing).AsTask());

        Assert.Equal(2, closing.Done);
        Assert.Equal("Stopping Claude in 2 tabs (2 of 2 done)", closing.Step);
    }
}
