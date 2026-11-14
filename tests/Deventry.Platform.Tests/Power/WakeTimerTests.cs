using System.Runtime.Versioning;
using Deventry.Platform.Power.Windows;

namespace Deventry.Platform.Tests.Power;

/// <summary>
/// Waking the computer for a timed message (DESIGN.md §18, "Timed messages", "Waking the computer"). The Windows timer is
/// set and cancelled for real, a day ahead, and never slept through: whether it wakes the computer is for a person to see.
/// </summary>
public class WakeTimerTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_a_wake_timer_is_set_moved_and_cancelled()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows' waitable timers.");
        using var timer = new WindowsWakeTimer();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1);

        await timer.WakeAtAsync(tomorrow, TestContext.Current.CancellationToken);

        Assert.Equal(tomorrow, timer.WakesAt);
        Assert.Contains("A Windows wake timer is set for", timer.Describe(), StringComparison.Ordinal);

        await timer.WakeAtAsync(tomorrow.AddHours(1), TestContext.Current.CancellationToken);
        Assert.Equal(tomorrow.AddHours(1), timer.WakesAt);

        await timer.WakeAtAsync(null, TestContext.Current.CancellationToken);
        Assert.Null(timer.WakesAt);
        Assert.Contains("No wake timer is set.", timer.Describe(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1u, 1u, true, null)]
    [InlineData(1u, 0u, false, null)]
    [InlineData(null, null, true, null)]
    [InlineData(1u, 0u, true, "only while it's plugged in")]
    [InlineData(1u, 2u, true, "only while it's plugged in")]
    [InlineData(0u, 0u, true, "doesn't let programs wake the computer, so")]
    [InlineData(2u, 1u, false, "doesn't let programs wake the computer, so")]
    [InlineData(0u, 1u, true, "doesn't let programs wake the computer while it's plugged in")]
    public void The_power_plan_says_when_Windows_wont_wake_for_it(uint? plugged, uint? onBattery, bool hasBattery, string? says)
    {
        var caveat = WindowsPowerPlan.CaveatFor(plugged, onBattery, hasBattery);

        if (says is null)
        {
            Assert.Null(caveat);
        }
        else
        {
            Assert.Contains(says, caveat, StringComparison.Ordinal);
            Assert.EndsWith("Allow wake timers is in Control Panel → Power Options → Change plan settings → Change advanced power settings → Sleep.", caveat, StringComparison.Ordinal);
        }
    }
}
