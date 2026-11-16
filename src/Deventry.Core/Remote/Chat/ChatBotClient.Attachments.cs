using Deventry.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Chat;

/// <summary>
/// Images and files sent to a tab (DESIGN.md §18, "Remote clients", "Attachments from a client"): an album gathered,
/// the files downloaded, one sent with no words held for the next message, and sent with the message's words.
/// </summary>
public abstract partial class ChatBotClient
{
    /// <summary>How long an album's pictures are waited for after the last came: Telegram sends one a message.</summary>
    public static readonly TimeSpan AlbumWait = TimeSpan.FromSeconds(2);

    /// <summary>How long an image or file sent with no words waits for the next message before it goes on its own.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(10);

    /// <summary>Downloads an attachment from the service; throws a <see cref="ChatApiException"/> when it can't.</summary>
    protected abstract Task<byte[]> DownloadAsync(ChatAttachment attachment, CancellationToken cancellationToken);

    /// <summary>An album's pictures so far, by chat and album.</summary>
    private readonly Dictionary<(long Chat, string Album), List<ChatMessage>> _albums = [];

    /// <summary>What's held for a chat's next message.</summary>
    private readonly Dictionary<long, Held> _held = [];

    /// <param name="RepliedTo">The bot's message the first held one replied to: the tab it's for.</param>
    /// <param name="ReplyTo">The user's first held message, which the bot's notice replies to.</param>
    private sealed record Held(List<RemoteAttachment> Files, long? RepliedTo, long? ReplyTo)
    {
        /// <summary>The bot's message saying what it holds, with <b>Send now</b> and <b>Discard</b>.</summary>
        public long? Notice { get; set; }
    }

    private sealed record SendHeldButton(long Chat) : Button;

    private sealed record DiscardHeldButton(long Chat) : Button;

    /// <summary>
    /// A message with words, images or files for a tab, once commands are handled: an album's picture waits for the
    /// rest, files are downloaded, and files with no words are held for the next message, when this client holds them.
    /// </summary>
    private async Task HandleContentAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        if (message.Album is { } album && message.Attachments.Count > 0)
        {
            var key = (chat, album);
            if (!_albums.TryGetValue(key, out var parts))
            {
                _albums[key] = parts = [];
            }
            parts.Add(message);
            After($"album:{chat}:{album}", AlbumWait, token => _albums.Remove(key, out var all) ? HandleContentAsync(Gathered(all), token) : Task.CompletedTask);
            return;
        }
        var text = message.Text?.Trim() ?? "";
        if (message.Attachments.Count == 0)
        {
            if (text.Length == 0)
            {
                return;
            }
            if (_awaiting.Remove(chat, out var waiting))
            {
                await waiting(text, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        else if (_awaiting.ContainsKey(chat))
        {
            await SendAsync(chat, Format.Escape(RemoteMessages.AnswerTakesWords), replyTo: ReplyTo(message), silent: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var files = await DownloadAllAsync(message, cancellationToken).ConfigureAwait(false);
        if (text.Length == 0 && files.Count == 0)
        {
            return;
        }
        if (text.Length == 0 && Desk.HoldsAttachments(Kind))
        {
            await HoldAsync(chat, files, message.ReplyTo, ReplyTo(message), cancellationToken).ConfigureAwait(false);
            return;
        }
        // What was held goes with these words, to the tab the first held one was for unless these say another.
        var held = await TakeHeldAsync(chat, RemoteMessages.SentWithMessage, cancellationToken).ConfigureAwait(false);
        await RouteAsync(chat, text, [.. held?.Files ?? [], .. files], message.ReplyTo ?? held?.RepliedTo, ReplyTo(message), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An album's pictures as one message: the caption one had, and every picture in order.</summary>
    private static ChatMessage Gathered(List<ChatMessage> parts) => parts[0] with
    {
        Text = parts.Select(p => p.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
        ReplyTo = parts.Select(p => p.ReplyTo).FirstOrDefault(r => r is not null),
        Attachments = [.. parts.SelectMany(p => p.Attachments)],
        Album = null,
    };

    /// <summary>
    /// The message's images and files, downloaded: up to <see cref="RemoteAttachment.MostPerMessage"/>, each up to
    /// <see cref="RemoteAttachment.MaxBytes"/>. What can't be had is said, in one reply.
    /// </summary>
    private async Task<List<RemoteAttachment>> DownloadAllAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        var files = new List<RemoteAttachment>();
        var problems = new List<string>();
        foreach (var attachment in message.Attachments.Take(RemoteAttachment.MostPerMessage))
        {
            if (attachment.Size is { } size && size > RemoteAttachment.MaxBytes)
            {
                problems.Add(RemoteMessages.TooLarge(attachment.Name, size));
                continue;
            }
            try
            {
                var data = await DownloadAsync(attachment, cancellationToken).ConfigureAwait(false);
                if (data.LongLength > RemoteAttachment.MaxBytes)
                {
                    problems.Add(RemoteMessages.TooLarge(attachment.Name, data.LongLength));
                    continue;
                }
                files.Add(new RemoteAttachment(attachment.Name, data));
            }
            catch (ChatApiException ex)
            {
                _logger.LogWarning("{Client}: couldn't download a file sent to it: {Error}", Name, ex.Message);
                problems.Add($"Couldn't get {attachment.Name} from {Name}: {ex.Message}");
            }
        }
        if (message.Attachments.Count > RemoteAttachment.MostPerMessage)
        {
            problems.Add(RemoteMessages.TooMany(message.Attachments.Count - RemoteAttachment.MostPerMessage));
        }
        if (problems.Count > 0)
        {
            await SendAsync(message.Chat, Format.Escape(string.Join("\n", problems)), replyTo: ReplyTo(message), silent: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        return files;
    }

    /// <summary>
    /// Holds files sent with no words for the chat's next message, saying so with <b>Send now</b> and <b>Discard</b>,
    /// until <see cref="HoldFor"/> after the last, when they go on their own.
    /// </summary>
    private async Task HoldAsync(long chat, List<RemoteAttachment> files, long? repliedTo, long? replyTo, CancellationToken cancellationToken)
    {
        if (!_held.TryGetValue(chat, out var held))
        {
            _held[chat] = held = new Held([], repliedTo, replyTo);
        }
        var room = RemoteAttachment.MostPerMessage - held.Files.Count;
        held.Files.AddRange(files.Take(Math.Max(0, room)));
        var images = held.Files.Count(f => MessageImage.DetectMediaType(f.Data) is not null);
        var text = Format.Escape(RemoteMessages.Holding(images, held.Files.Count - images, HoldFor));
        var buttons = Rows([MakeButton("Send now", new SendHeldButton(chat)), MakeButton("Discard", new DiscardHeldButton(chat))], 2);
        if (held.Notice is { } notice)
        {
            await PacedAsync(token => DeliverEditAsync(chat, notice, text, buttons, token), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            held.Notice = await SendAsync(chat, text, buttons, replyTo, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        After($"held:{chat}", HoldFor, token => SendHeldAsync(chat, timedOut: true, token));
    }

    /// <summary>
    /// Takes what's held for a chat, if anything, its notice saying <paramref name="said"/> without its buttons.
    /// </summary>
    private async Task<Held?> TakeHeldAsync(long chat, string said, CancellationToken cancellationToken)
    {
        if (!_held.Remove(chat, out var held))
        {
            return null;
        }
        if (_timers.Remove($"held:{chat}", out var timer))
        {
            timer.Dispose();
        }
        if (held.Notice is { } notice)
        {
            try
            {
                await PacedAsync(token => DeliverEditAsync(chat, notice, Format.Escape(said), null, token), cancellationToken).ConfigureAwait(false);
            }
            catch (ChatApiException ex)
            {
                _logger.LogInformation("{Client}: couldn't update the held files' message: {Error}", Name, ex.Message);
            }
        }
        return held;
    }

    /// <summary><b>Send now</b>, or nothing following in <see cref="HoldFor"/>: what's held goes on its own.</summary>
    private async Task SendHeldAsync(long chat, bool timedOut, CancellationToken cancellationToken)
    {
        var said = timedOut ? RemoteMessages.HeldAlone(HoldFor) : "Sent on its own.";
        if (await TakeHeldAsync(chat, said, cancellationToken).ConfigureAwait(false) is { } held)
        {
            await RouteAsync(chat, "", held.Files, held.RepliedTo, held.Notice, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Words and files to a tab: the one the message replied to a message of, the chat's target, or the one picked.</summary>
    /// <param name="repliedTo">The bot's message the user replied to.</param>
    /// <param name="replyTo">The user's message the bot's answer replies to.</param>
    private async Task RouteAsync(long chat, string text, IReadOnlyList<RemoteAttachment> files, long? repliedTo, long? replyTo, CancellationToken cancellationToken)
    {
        if (repliedTo is { } replied && _messageTabs.TryGetValue((chat, replied), out var tabId))
        {
            await SendToTabAsync(chat, tabId, text, files, replyTo, cancellationToken).ConfigureAwait(false);
        }
        else if (_targets.TryGetValue(chat, out var target))
        {
            await SendToTabAsync(chat, target, text, files, replyTo, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AskWhichTabAsync(chat, text, files, replyTo, cancellationToken).ConfigureAwait(false);
        }
    }
}
