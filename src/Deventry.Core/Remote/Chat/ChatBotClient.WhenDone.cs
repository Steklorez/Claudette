namespace Deventry.Core.Remote.Chat;

/// <summary>
/// The minute before the computer shuts down or goes to sleep once Claude is done (DESIGN.md §9, "When Claude is done"):
/// a message with <b>Cancel</b>, edited once it ends.
/// </summary>
public abstract partial class ChatBotClient
{
    private sealed record CancelWhenDoneButton : Button;

    /// <summary>The countdown's messages, a chat each, with their text, to edit once it ends.</summary>
    private readonly List<(long Chat, long Message, string Text)> _whenDoneMessages = [];

    /// <summary>Always said, even while quiet, since the computer is about to go: silently at the desk.</summary>
    private async Task OnWhenDoneCountingAsync(RemoteWhenDoneCounting counting, CancellationToken cancellationToken)
    {
        await EndWhenDoneMessagesAsync(null, cancellationToken).ConfigureAwait(false);
        var text = Format.Escape(RemoteMessages.WhenDoneCounting(Desk.MachineName, counting.Action, counting.At, _time.LocalTimeZone));
        var buttons = Rows([MakeButton("Cancel", new CancelWhenDoneButton())], 1);
        foreach (var (chat, message) in await BroadcastAsync(text, buttons, silent: counting.Notice != RemoteNotice.Sound, cancellationToken: cancellationToken)
                     .ConfigureAwait(false))
        {
            _whenDoneMessages.Add((chat, message, text));
        }
    }

    private Task OnWhenDoneEndedAsync(RemoteWhenDoneEnded ended, CancellationToken cancellationToken) =>
        EndWhenDoneMessagesAsync(Format.Italic(RemoteMessages.WhenDoneEnded(ended.Action, ended.How, ended.Client, ended.At, _time.LocalTimeZone)), cancellationToken);

    /// <summary>The countdown's messages lose their <b>Cancel</b>, ending with <paramref name="line"/> when there is one.</summary>
    private async Task EndWhenDoneMessagesAsync(string? line, CancellationToken cancellationToken)
    {
        var messages = _whenDoneMessages.ToArray();
        _whenDoneMessages.Clear();
        foreach (var (chat, message, text) in messages)
        {
            await EditAsync(chat, message, line is null ? text : $"{text}\n\n{line}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CancelWhenDoneAsync(ChatTap tap, CancellationToken cancellationToken)
    {
        var result = await Desk.CancelWhenDoneAsync(Name, cancellationToken).ConfigureAwait(false);
        await Toast(tap, result.Done ? "Cancelled." : result.Reason, cancellationToken).ConfigureAwait(false);
        // Done, its message is edited as the desk says it ended; otherwise it's over already, and just loses its button.
        if (!result.Done && tap is { Chat: { } chat, MessageId: { } message })
        {
            await PacedAsync(token => DeliverButtonsAsync(chat, message, null, token), cancellationToken).ConfigureAwait(false);
        }
    }
}
