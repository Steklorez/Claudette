using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core;
using Deventry.Core.TimedMessages;

namespace Deventry.App.Tests;

/// <summary>DESIGN.md §18, "Timed messages": sent at their times, while Deventry runs, to a tab of their own.</summary>
public class TimedMessagesTests
{
    private const string Text = "Triage the overnight issues.";

    /// <summary>A message once, at <paramref name="at"/> on the harness's clock, in its local time.</summary>
    private static TimedMessage Once(TabTestHarness h, DateTimeOffset at, string? folder = null)
    {
        var local = TimeZoneInfo.ConvertTime(at, h.Time.LocalTimeZone).DateTime;
        return new TimedMessage
        {
            Name = "Morning triage",
            Folder = folder ?? h.WorkFolder,
            Text = Text,
            Schedule = new TimedSchedule { Kind = TimedScheduleKind.Once, Date = DateOnly.FromDateTime(local), Time = TimeOnly.FromDateTime(local) },
        };
    }

    private static async Task<TabTestHarness> StartedAsync()
    {
        var h = new TabTestHarness();
        // The service starts as the tabs come back, before any test tab is opened.
        h.OnUiThread(() => h.Shell.Restore(null));
        await TabTestHarness.Eventually(() => !h.Shell.AllTabs.Any());
        return h;
    }

    private static IEnumerable<string> SentTexts(TabTestHarness h) =>
        h.Transport.Sent.Where(m => m["type"]?.GetValue<string>() == "user").Select(ScriptedTransport.UserText);

    private static IReadOnlyList<OsNotificationCopy> Notifications(TabTestHarness h)
    {
        lock (h.Notifier.Shown)
        {
            return [.. h.Notifier.Shown.Select(n => new OsNotificationCopy(n.Title, n.Body))];
        }
    }

    private sealed record OsNotificationCopy(string Title, string Body);

    [Fact]
    public async Task At_its_time_a_new_tab_opens_beside_the_one_in_use_and_sends_it()
    {
        await using var h = await StartedAsync();
        var inUse = await h.OpenTabAsync();
        var at = h.Time.GetUtcNow().AddMinutes(5);
        var message = Once(h, at);
        message.PermissionMode = "plan";
        message.Model = "claude-sonnet-5-5";
        h.OnUiThread(() => h.Services.TimedMessages.Save(message));

        h.Time.Advance(TimeSpan.FromMinutes(5));

        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the timed message sent");
        var timed = InlineDispatcher.Read(() => h.Shell.AllTabs.Single(t => t != inUse));
        Assert.Same(inUse, InlineDispatcher.Read(() => h.Shell.SelectedTab));
        Assert.Equal("plan", timed.State.Overrides.PermissionMode);
        Assert.Equal("claude-sonnet-5-5", timed.State.Overrides.Model);
        var card = InlineDispatcher.Read(() => timed.Items.OfType<UserMessageItem>().Single());
        Assert.Equal("Timed message: Morning triage", card.Label);
        Assert.Equal(Text, card.Text);
        await TabTestHarness.Eventually(() => Notifications(h).Any(n => n.Title == "Timed message sent: Morning triage in work" && n.Body == Text), "the notification");
        Assert.Equal(at, h.Services.State.TimedMessagesHandled[message.Id]);
    }

    [Fact]
    public async Task With_no_tab_in_use_the_new_tab_is_selected()
    {
        await using var h = await StartedAsync();
        h.OnUiThread(() => h.Services.TimedMessages.Save(Once(h, h.Time.GetUtcNow().AddMinutes(1))));

        h.Time.Advance(TimeSpan.FromMinutes(1));

        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the timed message sent");
        Assert.Same(InlineDispatcher.Read(() => h.Shell.AllTabs.Single()), InlineDispatcher.Read(() => h.Shell.SelectedTab));
    }

    [Fact]
    public async Task A_worktree_message_outside_git_opens_in_the_folder()
    {
        await using var h = await StartedAsync();
        var message = Once(h, h.Time.GetUtcNow().AddMinutes(1));
        message.InWorktree = true;
        h.OnUiThread(() => h.Services.TimedMessages.Save(message));

        h.Time.Advance(TimeSpan.FromMinutes(1));

        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the timed message sent");
        var tab = InlineDispatcher.Read(() => h.Shell.AllTabs.Single());
        Assert.Null(tab.State.NewWorktree);
    }

    [Fact]
    public async Task A_worktree_message_in_git_opens_a_worktree_tab_named_after_it()
    {
        Assert.SkipWhen(!WorktreeTabTests.GitInstalled, "git isn't on PATH.");
        await using var h = await StartedAsync();
        await WorktreeTabTests.InitRepositoryAsync(h);
        var message = Once(h, h.Time.GetUtcNow().AddMinutes(1));
        message.InWorktree = true;
        h.OnUiThread(() => h.Services.TimedMessages.Save(message));

        h.Time.Advance(TimeSpan.FromMinutes(1));

        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the timed message sent");
        var tab = InlineDispatcher.Read(() => h.Shell.AllTabs.Single());
        Assert.Equal("morning-triage", tab.State.NewWorktree);
    }

    [Fact]
    public async Task A_message_noticed_late_is_sent_under_half_an_hour_and_skipped_after()
    {
        await using var h = await StartedAsync();
        var soon = Once(h, h.Time.GetUtcNow().AddMinutes(5));
        var laterAt = h.Time.GetUtcNow().AddMinutes(60);
        var later = Once(h, laterAt);
        later.Name = "Build check";
        later.Text = "Check last night's build.";
        h.OnUiThread(() =>
        {
            h.Services.TimedMessages.Save(soon);
            h.Services.TimedMessages.Save(later);
        });

        // The computer slept through both times, and nothing woke it: its timers run as it wakes.
        h.Time.Advance(TimeSpan.FromMinutes(25));

        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the message 20 minutes late sent");

        h.Time.Advance(TimeSpan.FromMinutes(70));

        await TabTestHarness.Eventually(() => Notifications(h).Any(n => n.Title == "Timed message skipped: Build check"), "the skipped one's notification");
        Assert.DoesNotContain("Check last night's build.", SentTexts(h));
        Assert.EndsWith("went by while the computer was asleep.", Notifications(h).Single(n => n.Title == "Timed message skipped: Build check").Body, StringComparison.Ordinal);
        Assert.Single(InlineDispatcher.Read(() => h.Shell.AllTabs));
        var row = h.Services.TimedMessages.Rows.Single(r => r.Name == "Build check");
        Assert.StartsWith("Skipped the one ", row.Skipped);
        Assert.Equal(laterAt, h.Services.State.TimedMessagesSkipped[later.Id]);
    }

    [Fact]
    public async Task Clear_all_asks_first_and_then_deletes_every_message()
    {
        await using var h = await StartedAsync();
        var first = Once(h, h.Time.GetUtcNow().AddHours(1));
        var second = Once(h, h.Time.GetUtcNow().AddHours(2));
        second.Name = "Build check";
        h.OnUiThread(() =>
        {
            h.Services.TimedMessages.Save(first);
            h.Services.TimedMessages.Save(second);
        });
        Assert.Equal(2, h.Services.TimedMessages.Rows.Count);

        h.OnUiThread(() => h.Shell.ClearAllTimedMessagesCommand.Execute(null));

        var confirmation = Assert.IsType<ConfirmationViewModel>(InlineDispatcher.Read(() => h.Shell.Confirmation));
        Assert.Equal("Clear every timed message?", confirmation.Title);
        Assert.Equal(2, h.Services.TimedMessages.Rows.Count);
        await InlineDispatcher.RunAsync(() => confirmation.ConfirmCommand.ExecuteAsync(null));

        Assert.Empty(h.Services.TimedMessages.Rows);
        Assert.False(h.Services.TimedMessages.HasAny);
        Assert.Null(h.WakeTimer.WakesAt);
        Assert.Empty(h.Services.Settings.TimedMessages.Messages);
    }

    [Fact]
    public async Task A_message_whose_folder_is_gone_isnt_sent_and_says_why()
    {
        await using var h = await StartedAsync();
        var gone = Path.Combine(h.Root, "gone");
        h.OnUiThread(() => h.Services.TimedMessages.Save(Once(h, h.Time.GetUtcNow().AddMinutes(1), gone)));

        h.Time.Advance(TimeSpan.FromMinutes(1));

        await TabTestHarness.Eventually(() => Notifications(h).Any(n => n.Title == "Timed message not sent: Morning triage"), "the notification");
        Assert.Contains(gone, Notifications(h).Single().Body, StringComparison.Ordinal);
        Assert.Empty(InlineDispatcher.Read(() => h.Shell.AllTabs));
    }

    [Fact]
    public async Task The_computer_is_woken_a_minute_early_and_not_for_one_cleared()
    {
        await using var h = await StartedAsync();
        var at = h.Time.GetUtcNow().AddHours(3);
        var message = Once(h, at);

        h.OnUiThread(() => h.Services.TimedMessages.Save(message));

        Assert.Equal(at - TimedMessagePlanner.WakeEarly, h.WakeTimer.WakesAt);
        var timed = h.Services.TimedMessages;
        Assert.True(timed.HasAny);
        var row = Assert.Single(timed.Rows);
        Assert.Equal($"Morning triage · work · {Formats.Upcoming(at, h.Time)}", row.MenuText);
        Assert.Equal($"Next timed message: Morning triage in work, {Formats.Upcoming(at, h.Time)}.", timed.NextLine);
        Assert.EndsWith(timed.NextLine!, h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        h.OnUiThread(() => timed.Delete(message.Id));

        Assert.Null(h.WakeTimer.WakesAt);
        Assert.False(timed.HasAny);
        Assert.Empty(timed.Rows);
        Assert.Null(timed.NextLine);
        Assert.Empty(h.Services.Settings.TimedMessages.Messages);
    }

    [Fact]
    public async Task A_message_turned_off_isnt_sent_nor_woken_for()
    {
        await using var h = await StartedAsync();
        var message = Once(h, h.Time.GetUtcNow().AddMinutes(5));
        h.OnUiThread(() => h.Services.TimedMessages.Save(message));

        h.OnUiThread(() => h.Services.TimedMessages.SetOn(message.Id, false));
        h.Time.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(h.WakeTimer.WakesAt);
        Assert.False(h.Services.TimedMessages.HasAny);
        Assert.Equal("Morning triage · work · off", Assert.Single(h.Services.TimedMessages.Rows).MenuText);
        Assert.Empty(SentTexts(h));
    }

    [Fact]
    public async Task Sleep_is_held_off_from_two_minutes_before_until_its_tabs_turn_ends()
    {
        await using var h = await StartedAsync();
        h.OnUiThread(() => h.Services.TimedMessages.Save(Once(h, h.Time.GetUtcNow().AddMinutes(10))));
        Assert.False(h.SleepBlocker.IsBlocking);

        h.Time.Advance(TimeSpan.FromMinutes(8));
        await TabTestHarness.Eventually(() => h.SleepBlocker.IsBlocking, "sleep held off before its time");
        Assert.Contains("A timed message is due.", h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        h.Time.Advance(TimeSpan.FromMinutes(2));
        await TabTestHarness.Eventually(() => SentTexts(h).Contains(Text), "the timed message sent");
        var tab = InlineDispatcher.Read(() => h.Shell.AllTabs.Single());
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "its turn");
        Assert.True(h.SleepBlocker.IsBlocking);
        Assert.Contains("A timed message's tab is working.", h.Services.KeepAwake.Tooltip, StringComparison.Ordinal);

        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "its turn's end");
        // Two minutes more, as for any of Claude's work.
        Assert.True(h.SleepBlocker.IsBlocking);
        await TabTestHarness.Eventually(() => !h.SleepBlocker.IsBlocking, "the computer let sleep", poll: () => h.Time.Advance(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Closing_Deventry_takes_the_wake_away()
    {
        var h = await StartedAsync();
        h.OnUiThread(() => h.Services.TimedMessages.Save(Once(h, h.Time.GetUtcNow().AddHours(1))));

        await h.DisposeAsync();

        Assert.True(h.WakeTimer.IsDisposed);
    }
}
