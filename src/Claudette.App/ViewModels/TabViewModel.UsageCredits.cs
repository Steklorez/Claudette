using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.Core.Sessions;
using Claudette.Usage;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// A tab that doesn't use usage credits (DESIGN.md §6, "Tabs that don't use usage credits"): while they pay for Claude,
/// it holds what it would send until the limit resets, and stops a turn they begin paying for. Either way it waits for
/// the reset as it does after a usage limit (<see cref="AutoContinueMonitor"/>), and a message it held goes in the
/// continue's place.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The turn under way may use usage credits: the user sent a held message anyway, with <b>Send now</b>.</summary>
    private bool _creditsAllowedThisTurn;

    /// <summary>The turn is being stopped for usage credits: the turn's later events don't stop it again.</summary>
    private bool _stoppingForCredits;

    /// <summary>The tab's own choice, else Settings → Usage.</summary>
    public bool UsesUsageCredits => State.Overrides.UseUsageCredits ?? _services.Settings.Usage.UseUsageCredits;

    /// <summary>Only the Default account's plan has usage credits: an API key pays for every request (DESIGN.md §4, "Accounts").</summary>
    public bool HasUsageCreditsMenu => Account is null;

    public const string UsageCreditsMenuTip =
        "Once your session or weekly limit is used up, carry on with your plan's usage credits. Off, the tab stops and waits for the limit to reset.";

    /// <summary>The tab doesn't use usage credits, and runs where they'd pay for it.</summary>
    private bool RefusesCredits => Account is null && !UsesUsageCredits && !_creditsAllowedThisTurn;

    /// <summary>
    /// Usage credits pay for Claude now, by the header's latest reading, and the tab doesn't use them: the limit that's
    /// used up, and its reset. Null when they don't, or when the reset isn't known: then a turn is stopped once it starts.
    /// </summary>
    private (DateTimeOffset ResetsAt, string? LimitType)? CreditsBlocking =>
        RefusesCredits && CreditUse.From(_services.Usage?.Current, _services.Time.GetUtcNow()) is { Until: { } until } use
            ? (until, use.LimitType)
            : null;

    /// <summary>A message the tab held for usage credits goes at the reset, rather than the continue.</summary>
    private bool HasHeldForCredits => _held.Any(h => h.Card.IsHeldForCredits);

    /// <summary><b>Use usage credits</b> in the tab's menu.</summary>
    [RelayCommand]
    private void ToggleUseUsageCredits() => SetUsesUsageCredits(!UsesUsageCredits);

    /// <summary><b>Use usage credits</b> on the bar of a tab waiting for the reset: they pay for it from now on, and it carries on.</summary>
    [RelayCommand]
    private void UseUsageCreditsNow() => SetUsesUsageCredits(true);

    /// <summary>The tab's own choice, kept only while it differs from Settings → Usage.</summary>
    private void SetUsesUsageCredits(bool on)
    {
        State.Overrides.UseUsageCredits = on == _services.Settings.Usage.UseUsageCredits ? null : on;
        _services.SaveState();
        OnPropertyChanged(nameof(HasOverrides));
        OnUsageCreditsChoiceChanged();
    }

    /// <summary>
    /// The tab's choice or Settings → Usage changed. A tab that uses them now stops waiting for the reset: one that
    /// would have continued by itself carries on now, and what it held for them goes.
    /// </summary>
    private void OnUsageCreditsChoiceChanged()
    {
        OnPropertyChanged(nameof(UsesUsageCredits));
        if (!UsesUsageCredits)
        {
            return;
        }
        var continues = _autoContinue.Wait is { IsForCredits: true, WillContinue: true };
        _autoContinue.CreditsAllowed();
        if (!continues && !IsWorking && TakeHeldForCredits() is { } held)
        {
            _ = SendRawAsync(held.Text, held.Images, held.Suffix, held.Stamp);
        }
    }

    /// <summary>
    /// Holds a message while usage credits pay and the tab doesn't use them: it goes when the limit resets, and the tab
    /// waits for that as after a usage limit. Returns whether it held it.
    /// </summary>
    private bool HoldForCredits(UserMessageItem card, PendingMessage message)
    {
        if (CreditsBlocking is not { } blocking)
        {
            return false;
        }
        Hold(card, message);
        card.IsHeldForCredits = true;
        _autoContinue.HoldingForCredits(blocking.ResetsAt, blocking.LimitType);
        OnPropertyChanged(nameof(LimitWaitText));
        return true;
    }

    /// <summary>
    /// The turn ended with messages held until then, and usage credits pay: they wait for the reset instead. Returns
    /// whether they do.
    /// </summary>
    private bool HoldHeldForCredits()
    {
        if (CreditsBlocking is not { } blocking)
        {
            return false;
        }
        foreach (var (card, _) in _held)
        {
            card.IsHeldForCredits = true;
        }
        _autoContinue.HoldingForCredits(blocking.ResetsAt, blocking.LimitType);
        OnPropertyChanged(nameof(LimitWaitText));
        return true;
    }

    /// <summary>
    /// The first message held for usage credits, taken off the list to send; the others wait for its turn to end, as
    /// held messages do. Null when there's none.
    /// </summary>
    private PendingMessage? TakeHeldForCredits()
    {
        var index = _held.FindIndex(h => h.Card.IsHeldForCredits);
        if (index < 0)
        {
            return null;
        }
        var (card, message) = _held[index];
        _held.RemoveAt(index);
        foreach (var (other, _) in _held)
        {
            other.IsHeldForCredits = false;
        }
        card.IsHeldForCredits = false;
        card.IsHeld = false;
        OnHeldMessageSent(card);
        return message;
    }

    /// <summary>
    /// A <c>rate_limit_event</c> during a turn: when it says usage credits are paying (<c>isUsingOverage</c>,
    /// undocumented) and the tab doesn't use them, the turn stops, and waits for the limit to reset.
    /// </summary>
    private void StopIfCreditsPay(JsonObject info)
    {
        if (!RefusesCredits || _stoppingForCredits || !IsInTurn || _session is null)
        {
            return;
        }
        var now = _services.Time.GetUtcNow();
        if (UsageParser.FromRateLimitEvent(info, now)?.CreditsReport is not { IsUsing: true } report)
        {
            return;
        }
        _stoppingForCredits = true;
        var resetsAt = report.ResetsAt ?? CreditUse.From(_services.Usage?.Current, now)?.Until;
        _ = StopForCreditsAsync(_session, resetsAt, report.LimitType);
    }

    /// <summary>
    /// Stops the turn, with what was waiting its turn in Claude Code, which would run on usage credits too: the user's
    /// messages and the thread's are held for the reset, Claudette's own check-ins dropped.
    /// </summary>
    private async Task StopForCreditsAsync(ClaudeSession session, DateTimeOffset? resetsAt, string? limitType)
    {
        try
        {
            var receipt = await session.InterruptAsync(cancelQueued: true);
            HoldCancelledForCredits(receipt.Cancelled);
        }
        catch (Exception ex)
        {
            _conversation.AddNote($"Couldn't stop Claude when usage credits began paying for it: {ex.Message}", NoteKind.Error);
            return;
        }
        _conversation.AddNote("Usage credits began paying for Claude, and this tab doesn't use them, so Claudette stopped the turn.", NoteKind.Warning);
        if (resetsAt is { } reset)
        {
            _autoContinue.StoppedForCredits(reset, limitType);
        }
    }

    private void HoldCancelledForCredits(IReadOnlyCollection<string> ids)
    {
        var dropped = new List<string>();
        foreach (var id in ids)
        {
            var card = Items.OfType<UserMessageItem>().FirstOrDefault(m => m.SentId == id);
            var pending = _awaitingReply.Find(p => p.Stamp?.Uuid == id);
            if (card is null || pending is null || card.IsCheckIn || card.IsAutoContinue)
            {
                dropped.Add(id);
                continue;
            }
            _awaitingReply.Remove(pending);
            Hold(card, pending);
            card.IsHeldForCredits = true;
        }
        TakeBack(dropped);
        OnPropertyChanged(nameof(LimitWaitText));
    }
}
