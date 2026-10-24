using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.Usage;

namespace Claudette.App.Tests;

/// <summary>
/// Tabs that don't use usage credits (DESIGN.md §6): they stop a turn credits begin paying for, and hold what they'd
/// send while credits pay, waiting for the limit to reset as after a usage limit.
/// </summary>
public class UsageCreditsTabTests
{
    [Fact]
    public async Task A_tab_that_doesnt_use_them_stops_the_turn_they_begin_paying_for_and_continues_at_the_reset()
    {
        await using var h = new TabTestHarness();
        var tab = await WorkingAsync(h);
        tab.ToggleUseUsageCreditsCommand.Execute(null);
        Assert.False(tab.UsesUsageCredits);
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(2);

        h.Transport.Emit(PaidByCredits(reset));

        await TabTestHarness.Eventually(() => h.Transport.SentControlSubtypes.Contains("interrupt"), "the interrupt");
        h.Transport.Emit(Stopped());
        await TabTestHarness.Eventually(() => tab.HasLimitWait && tab.Status == TabStatus.Idle, "the wait");
        Assert.Equal(LimitWaitCause.CreditsOff, tab.LimitWait?.Cause);
        Assert.Equal(
            $"Your session limit is used up, and this tab doesn't use usage credits, so Claude stopped. The task continues by itself when it resets, {Formats.When(reset, h.Time)}.",
            tab.LimitWaitText);
        Assert.True(tab.CanUseCreditsNow);
        Assert.True(tab.CanStopContinuing);
        Assert.Contains(tab.Items.OfType<NoteItem>(), n => n.Text == "Usage credits began paying for Claude, and this tab doesn't use them, so Claudette stopped the turn.");

        await AutoContinueTests.AdvanceToAsync(h, reset + AutoContinueMonitor.Grace);

        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains(AutoContinueMonitor.Message), "the continue");
        Assert.False(tab.HasLimitWait);
    }

    [Fact]
    public async Task A_tab_that_uses_them_carries_on()
    {
        await using var h = new TabTestHarness();
        var tab = await WorkingAsync(h);
        Assert.True(tab.UsesUsageCredits);

        h.Transport.Emit(PaidByCredits(h.Time.GetUtcNow() + TimeSpan.FromHours(2)));
        h.Transport.Emit(Done());

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle, "the turn to end");
        Assert.DoesNotContain("interrupt", h.Transport.SentControlSubtypes);
        Assert.False(tab.HasLimitWait);
    }

    [Fact]
    public async Task What_waited_its_turn_in_Claude_Code_is_held_for_the_reset_and_goes_in_place_of_the_continue()
    {
        await using var h = new TabTestHarness(s => s.Usage.UseUsageCredits = false);
        var tab = await WorkingAsync(h, "interrupt_receipt_v1", "interrupt_cancel_queued_v1");
        tab.ComposerText = "and the tests";
        await tab.SendCommand.ExecuteAsync(null);
        var queued = Card(tab, "and the tests");
        h.Transport.Answers["interrupt"] = _ => new JsonObject { ["still_queued"] = new JsonArray(), ["cancelled"] = new JsonArray(queued.SentId) };
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(2);

        h.Transport.Emit(PaidByCredits(reset));
        await TabTestHarness.Eventually(() => queued.IsHeldForCredits, "the message held");
        h.Transport.Emit(Stopped());
        await TabTestHarness.Eventually(() => tab.HasLimitWait && tab.Status == TabStatus.Idle, "the wait");

        Assert.Contains(queued, tab.Items);
        Assert.Equal("Queued: sent when the usage limit resets", queued.QueuedText);
        Assert.EndsWith($"so Claude stopped. Your message is sent when it resets, {Formats.When(reset, h.Time)}.", tab.LimitWaitText);

        await AutoContinueTests.AdvanceToAsync(h, reset + AutoContinueMonitor.Grace);

        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Count(t => t == "and the tests") == 2, "the message again");
        Assert.DoesNotContain(AutoContinueMonitor.Message, h.Transport.SentUserTexts);
        Assert.False(queued.IsHeld);
    }

    [Fact]
    public async Task A_message_sent_while_credits_pay_waits_for_the_reset()
    {
        await using var h = new TabTestHarness(s =>
        {
            s.Usage.UseUsageCredits = false;
            // The user's own message goes at the reset, whatever auto-continue says.
            s.Usage.ContinueAfterLimitReset = false;
        });
        await using var tracker = Tracker(h);
        var reset = await CreditsPayingAsync(h, tracker);
        var tab = await h.OpenTabAsync();

        tab.ComposerText = "Refactor the parser";
        await tab.SendCommand.ExecuteAsync(null);

        var held = Card(tab, "Refactor the parser");
        Assert.True(held.IsHeldForCredits);
        Assert.Equal("Sends it now, paid for with usage credits", held.SendNowTip);
        Assert.DoesNotContain("Refactor the parser", h.Transport.SentUserTexts);
        await TabTestHarness.Eventually(() => tab.HasLimitWait, "the wait");
        Assert.Equal(LimitWaitCause.MessageHeld, tab.LimitWait?.Cause);
        Assert.Equal(
            $"Your session limit is used up, and this tab doesn't use usage credits. Your message is sent when it resets, {Formats.When(reset, h.Time)}.",
            tab.LimitWaitText);
        // Its card's Cancel takes it back: the bar has no Don't continue or close of its own.
        Assert.False(tab.CanStopContinuing);
        Assert.False(tab.CanDismissLimitWait);
        Assert.StartsWith("Usage limit · continues at ", tab.RowDetail);

        await AutoContinueTests.AdvanceToAsync(h, reset + AutoContinueMonitor.Grace);

        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("Refactor the parser"), "the message");
        Assert.DoesNotContain(AutoContinueMonitor.Message, h.Transport.SentUserTexts);
        Assert.False(tab.HasLimitWait);
    }

    [Fact]
    public async Task Send_now_sends_it_anyway_and_its_turn_isnt_stopped()
    {
        await using var h = new TabTestHarness(s => s.Usage.UseUsageCredits = false);
        await using var tracker = Tracker(h);
        var reset = await CreditsPayingAsync(h, tracker);
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "This one matters";
        await tab.SendCommand.ExecuteAsync(null);
        var held = Card(tab, "This one matters");

        await tab.SendHeldNowCommand.ExecuteAsync(held);

        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("This one matters") && tab.Status == TabStatus.Working, "the message");
        Assert.False(held.IsHeldForCredits);
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(PaidByCredits(reset));
        h.Transport.Emit(Done());
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle, "the turn to end");
        Assert.DoesNotContain("interrupt", h.Transport.SentControlSubtypes);
        Assert.False(tab.HasLimitWait);

        // The next message is held again.
        tab.ComposerText = "And this one waits";
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(Card(tab, "And this one waits").IsHeldForCredits);
    }

    [Fact]
    public async Task Cancel_takes_back_a_held_message_and_ends_its_wait()
    {
        await using var h = new TabTestHarness(s => s.Usage.UseUsageCredits = false);
        await using var tracker = Tracker(h);
        await CreditsPayingAsync(h, tracker);
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "Never mind";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.HasLimitWait, "the wait");

        await tab.CancelQueuedMessageCommand.ExecuteAsync(Card(tab, "Never mind"));

        await TabTestHarness.Eventually(() => !tab.HasLimitWait, "the wait to end");
        Assert.Equal("Never mind", tab.ComposerText);
    }

    [Fact]
    public async Task Use_usage_credits_on_the_bar_carries_on_now()
    {
        await using var h = new TabTestHarness(s => s.Usage.UseUsageCredits = false);
        await using var tracker = Tracker(h);
        await CreditsPayingAsync(h, tracker);
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "Refactor the parser";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.CanUseCreditsNow, "the wait");

        tab.UseUsageCreditsNowCommand.Execute(null);

        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("Refactor the parser"), "the message");
        Assert.True(tab.UsesUsageCredits);
        Assert.True(tab.State.Overrides.UseUsageCredits);
        Assert.True(tab.HasOverrides);
        Assert.False(tab.HasLimitWait);
    }

    [Fact]
    public async Task The_menu_keeps_the_tabs_own_choice_only_while_it_differs_from_Settings()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Assert.True(tab.HasUsageCreditsMenu);

        tab.ToggleUseUsageCreditsCommand.Execute(null);
        Assert.False(tab.State.Overrides.UseUsageCredits);
        Assert.True(tab.HasOverrides);

        tab.ToggleUseUsageCreditsCommand.Execute(null);
        Assert.Null(tab.State.Overrides.UseUsageCredits);
        Assert.False(tab.HasOverrides);

        // Settings → Usage turned off: tabs follow it, and one can turn them back on.
        var settings = new SettingsViewModel(h.Services, null);
        settings.Tabs.UseUsageCredits = false;
        await TabTestHarness.Eventually(() => !tab.UsesUsageCredits, "the tab to follow Settings");
        tab.ToggleUseUsageCreditsCommand.Execute(null);
        Assert.True(tab.State.Overrides.UseUsageCredits);
    }

    [Fact]
    public async Task Tab_settings_choose_it_too()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var settings = new TabSettingsViewModel(h.Services, tab, () => { });
        Assert.True(settings.ShowsUsageCredits);
        Assert.Equal("Default (on)", settings.SelectedUsageCredits.Label);

        settings.SelectedUsageCredits = settings.UsageCreditsChoices.Single(c => c.Label == "Off");
        await settings.DoneCommand.ExecuteAsync(null);

        Assert.False(tab.State.Overrides.UseUsageCredits);
        Assert.False(tab.UsesUsageCredits);
        settings = new TabSettingsViewModel(h.Services, tab, () => { });
        settings.UseAllDefaults();
        await settings.DoneCommand.ExecuteAsync(null);
        Assert.Null(tab.State.Overrides.UseUsageCredits);
    }

    [Fact]
    public async Task A_tab_on_an_API_key_has_no_usage_credits_to_stop_for()
    {
        await using var h = new TabTestHarness(s => s.Usage.UseUsageCredits = false);
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        h.Services.Accounts.HelperExecutable = Path.Combine(h.Root, "Claudette.exe");
        h.Services.Accounts.NewFolderAccount = (await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work")).Id;
        var tab = await WorkingAsync(h);

        Assert.False(tab.HasUsageCreditsMenu);
        Assert.False(new TabSettingsViewModel(h.Services, tab, () => { }).ShowsUsageCredits);
        h.Transport.Emit(PaidByCredits(h.Time.GetUtcNow() + TimeSpan.FromHours(2)));
        h.Transport.Emit(Done());
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle, "the turn to end");
        Assert.DoesNotContain("interrupt", h.Transport.SentControlSubtypes);
    }

    private static async Task<TabViewModel> WorkingAsync(TabTestHarness h, params string[] capabilities)
    {
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "Refactor the parser";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = "claude-opus-5-5",
            ["capabilities"] = new JsonArray([.. capabilities.Select(c => JsonValue.Create(c))]),
        });
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working && tab.State.SessionId == "s1", "working");
        return tab;
    }

    /// <summary>The header's usage, which the tabs read.</summary>
    private static UsageTracker Tracker(TabTestHarness h)
    {
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        return tracker;
    }

    /// <summary>The header hears that usage credits pay for Claude until the session limit resets, two hours from now.</summary>
    private static async Task<DateTimeOffset> CreditsPayingAsync(TabTestHarness h, UsageTracker tracker)
    {
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(2);
        var info = PaidByCredits(reset)["rate_limit_info"]!.AsObject();
        tracker.OnRateLimitEvent(new RateLimitEventMessage(info, new JsonObject { ["type"] = "rate_limit_event", ["rate_limit_info"] = info.DeepClone() }));
        await TabTestHarness.Eventually(() => CreditUse.From(tracker.Current, h.Time.GetUtcNow()) is not null, "credits paying");
        return reset;
    }

    /// <summary>What Claude Code sends once usage credits pay for a request: the limit's rejection, which the turn gets past.</summary>
    private static JsonObject PaidByCredits(DateTimeOffset reset) => new()
    {
        ["type"] = "rate_limit_event",
        ["rate_limit_info"] = new JsonObject
        {
            ["status"] = "rejected", ["resetsAt"] = reset.ToUnixTimeSeconds(), ["rateLimitType"] = "five_hour",
            ["overageStatus"] = "allowed", ["isUsingOverage"] = true,
            ["unifiedWindows"] = new JsonObject
            {
                ["five_hour"] = new JsonObject { ["utilization"] = 1.0, ["resetsAt"] = reset.ToUnixTimeSeconds() },
                ["seven_day"] = new JsonObject { ["utilization"] = 0.6, ["resetsAt"] = reset.AddDays(3).ToUnixTimeSeconds() },
            },
        },
        ["uuid"] = "rl1",
        ["session_id"] = "s1",
    };

    private static JsonObject Done() => new() { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "Done.", ["session_id"] = "s1" };

    /// <summary>The turn's end after the interrupt.</summary>
    private static JsonObject Stopped() => new()
    {
        ["type"] = "result", ["subtype"] = "error_during_execution", ["is_error"] = true, ["terminal_reason"] = "aborted_streaming", ["session_id"] = "s1",
    };

    private static UserMessageItem Card(TabViewModel tab, string text) => tab.Items.OfType<UserMessageItem>().Single(m => m.Text == text);
}
