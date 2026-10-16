using Claudette.App.Conversation;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Messages Claudette holds until the turn ends (DESIGN.md §5, "Queued messages"), with Settings → General →
/// <b>Messages sent while Claude works</b> set to <b>Queue</b>: each goes as the next turn, one per turn, rather than
/// into the turn under way.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The held messages, oldest first, each with its card.</summary>
    private readonly List<(UserMessageItem Card, PendingMessage Message)> _held = [];

    /// <summary>A message sent now is held rather than sent: Claude is working, and Settings says to queue.</summary>
    private bool HoldsWhileWorking => IsWorking && _services.Settings.General.MessagesWhileWorking == WhileWorking.Queue;

    /// <summary>Holds a message the user sent while Claude works, to go once the turn ends.</summary>
    private void Hold(UserMessageItem card, PendingMessage message)
    {
        card.IsQueued = true;
        card.IsHeld = true;
        _held.Add((card, message));
    }

    /// <summary>
    /// The turn ended: the oldest held message goes, as the next turn. Not while a usage limit stopped the turn, which
    /// would stop it too; they wait for the next turn to end, or for <b>Send now</b>. Nor while usage credits pay and the
    /// tab doesn't use them: they wait for the limit to reset (DESIGN.md §6, "Tabs that don't use usage credits").
    /// </summary>
    private void SendNextHeld()
    {
        if (_held.Count == 0 || _autoContinue.Wait is not null || _session is null || HoldHeldForCredits())
        {
            return;
        }
        var (card, message) = _held[0];
        _held.RemoveAt(0);
        card.IsHeld = false;
        OnHeldMessageSent(card);
        _ = SendRawAsync(message.Text, message.Images, message.Suffix, message.Stamp);
    }

    /// <summary><b>Send now</b> on a held message: it goes at once, into the turn if Claude is still working.</summary>
    [RelayCommand]
    private Task SendHeldNowAsync(UserMessageItem? card)
    {
        var index = _held.FindIndex(h => ReferenceEquals(h.Card, card));
        if (index < 0)
        {
            return Task.CompletedTask;
        }
        var (held, message) = _held[index];
        _held.RemoveAt(index);
        held.IsHeld = false;
        held.IsQueued = IsWorking;
        if (held.IsHeldForCredits)
        {
            // Sent anyway: usage credits pay for its turn (DESIGN.md §6, "Tabs that don't use usage credits").
            held.IsHeldForCredits = false;
            _creditsAllowedThisTurn = true;
            HeldForCreditsChanged();
        }
        OnHeldMessageSent(held);
        return SendRawAsync(message.Text, message.Images, message.Suffix, message.Stamp);
    }

    /// <summary>Takes back a held message: it was never sent, so Claude Code isn't asked.</summary>
    private bool TakeBackHeld(UserMessageItem card)
    {
        var index = _held.FindIndex(h => ReferenceEquals(h.Card, card));
        if (index < 0 || card.SentId is not { } id)
        {
            return false;
        }
        _held.RemoveAt(index);
        TakeBack([id]);
        HeldForCreditsChanged();
        return true;
    }

    /// <summary>The ids of every held message, taken off the list: Stop, or Claude Code exiting, takes them all back.</summary>
    private List<string> TakeAllHeld()
    {
        var ids = _held.Select(h => h.Card.SentId).OfType<string>().ToList();
        _held.Clear();
        HeldForCreditsChanged();
        return ids;
    }

    /// <summary>A message held for usage credits went, or was taken back: a wait only for them ends with the last.</summary>
    private void HeldForCreditsChanged()
    {
        if (!HasHeldForCredits)
        {
            _autoContinue.HeldMessagesGone();
        }
        OnPropertyChanged(nameof(LimitWaitText));
    }
}
