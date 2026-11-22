using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Chat;

/// <summary>What happens at the desk, as the bot's messages (DESIGN.md §18, "What the bot sends").</summary>
public abstract partial class ChatBotClient
{
    /// <summary>The least time between two edits of a live status message.</summary>
    public static readonly TimeSpan LiveEditInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often a live status message's time moves on while nothing else changes.</summary>
    public static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>The tabs the user sent a message to from here: their prompts and their turn's end always come.</summary>
    private readonly HashSet<string> _engaged = new(StringComparer.Ordinal);

    /// <summary>The messages each waiting prompt was sent in, by tab and prompt, to edit once it closes.</summary>
    private readonly Dictionary<string, List<PromptCopy>> _promptMessages = new(StringComparer.Ordinal);

    /// <summary>Each tab's live status message: the last one sent.</summary>
    private readonly Dictionary<string, Live> _live = new(StringComparer.Ordinal);

    /// <summary>How many prompt and live status messages have gone, so which of two came later in a chat is known.</summary>
    private long _order;

    /// <summary>A message a prompt was sent in, with its text, and when it went among the prompts and live statuses.</summary>
    private sealed record PromptCopy(long Chat, long Message, string Text, long Order);

    private sealed class Live(long chat, long message, string heading, RemoteTab tab)
    {
        public long Chat { get; } = chat;

        public long Message { get; } = message;

        /// <summary>Its first line, as the service writes it: "Sent to <b>Grappling hook</b>.", or the tab's name.</summary>
        public string Heading { get; } = heading;

        public RemoteTab Tab { get; set; } = tab;

        public long Order { get; init; }

        public DateTimeOffset LastEdit { get; set; }

        public string LastText { get; set; } = "";
    }

    private static string PromptKey(string tabId, string promptId) => $"{tabId}\n{promptId}";

    /// <summary>The messages the prompt was sent in to <paramref name="chat"/>, oldest first.</summary>
    private IEnumerable<PromptCopy> CopiesIn(long chat, string tabId, string promptId) =>
        _promptMessages.TryGetValue(PromptKey(tabId, promptId), out var copies) ? copies.Where(c => c.Chat == chat) : [];

    private void OnDeskEvent(RemoteEvent raised) => Post(token => HandleEventAsync(raised, token));

    /// <summary>
    /// Whether a message goes for an event, and how: as the desk says, held by <c>/quiet</c>; but a tab the user sent a
    /// message to from here always answers, silently while quiet.
    /// </summary>
    private RemoteNotice Effective(RemoteNotice notice, string? tabId) =>
        tabId is not null && _engaged.Contains(tabId) ? IsQuiet ? RemoteNotice.Silent : RemoteNotice.Sound
        : IsQuiet ? RemoteNotice.None
        : notice;

    private async Task HandleEventAsync(RemoteEvent raised, CancellationToken cancellationToken)
    {
        switch (raised)
        {
            case RemoteTabChanged changed:
                OnTabChanged(changed.Tab);
                break;
            case RemoteTabClosed closed:
                await OnTabClosedAsync(closed, cancellationToken).ConfigureAwait(false);
                break;
            case RemotePromptWaiting waiting:
                await OnPromptWaitingAsync(waiting, cancellationToken).ConfigureAwait(false);
                break;
            case RemotePromptClosed closed:
                await OnPromptClosedAsync(closed, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteTurnFinished turn:
                await OnTurnFinishedAsync(turn, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteTabFailed failed:
                await OnTabFailedAsync(failed, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteUsageAlert alert when Effective(alert.Notice, null) is var notice and not RemoteNotice.None:
                await BroadcastAsync($"{Format.Bold(alert.Title)}\n{Format.Escape(alert.Body)}", silent: notice == RemoteNotice.Silent, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                break;
            case RemoteProjectAction action when Effective(action.Notice, null) is var notice and not RemoteNotice.None:
                await BroadcastAsync($"{Format.Bold(action.Title)}\n{Format.Escape(action.Body)}", silent: notice == RemoteNotice.Silent, tabId: action.TabId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case RemoteDeskClosing closing:
                // Always said: silently at the desk, where the user is closing it.
                await BroadcastAsync(Format.Escape(RemoteMessages.Closing(Desk.MachineName)), silent: closing.Notice != RemoteNotice.Sound,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case RemoteWhenDoneCounting counting:
                await OnWhenDoneCountingAsync(counting, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteWhenDoneEnded ended:
                await OnWhenDoneEndedAsync(ended, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task OnTabClosedAsync(RemoteTabClosed closed, CancellationToken cancellationToken)
    {
        _engaged.Remove(closed.TabId);
        foreach (var chat in _targets.Where(t => t.Value == closed.TabId).Select(t => t.Key).ToArray())
        {
            _targets.Remove(chat);
        }
        var line = Format.Italic(closed.Kept ? RemoteMessages.NowKept : "The tab closed.");
        if (_live.Remove(closed.TabId, out var live))
        {
            _timers.Remove($"live:{closed.TabId}", out var timer);
            timer?.Dispose();
            await EditAsync(live.Chat, live.Message, $"{live.Heading}\n{line}", null, cancellationToken).ConfigureAwait(false);
        }
        // Its prompts still waiting lose their buttons: they're answered at the desk now.
        foreach (var key in _promptMessages.Keys.Where(k => k.StartsWith(PromptKey(closed.TabId, ""), StringComparison.Ordinal)).ToArray())
        {
            _promptMessages.Remove(key, out var copies);
            foreach (var copy in copies ?? [])
            {
                await EditAsync(copy.Chat, copy.Message, $"{copy.Text}\n\n{line}", null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    private async Task OnPromptWaitingAsync(RemotePromptWaiting waiting, CancellationToken cancellationToken)
    {
        var notice = Effective(waiting.Notice, waiting.Tab.Id);
        List<long> chats = notice == RemoteNotice.None ? [] : [.. await PairedChatsAsync(cancellationToken).ConfigureAwait(false)];
        // A chat following the tab's live status gets it whatever the desk says: the status there says it needs you.
        if (_live.TryGetValue(waiting.Tab.Id, out var live) && !chats.Contains(live.Chat))
        {
            chats.Add(live.Chat);
        }
        // Sent to a chat before, it isn't again: when the user goes away, or quiet ends.
        chats.RemoveAll(chat => CopiesIn(chat, waiting.Tab.Id, waiting.Prompt.Id).Any());
        if (chats.Count > 0)
        {
            await SendPromptAsync(chats, waiting.Tab, waiting.Prompt, Desk.Detail, notice != RemoteNotice.Sound, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a waiting prompt to <paramref name="chats"/>, with its buttons, and keeps its messages to edit once it closes.
    /// Returns how many chats it reached.
    /// </summary>
    private async Task<int> SendPromptAsync(IReadOnlyList<long> chats, RemoteTab tab, RemotePrompt prompt, RemoteDetail detail, bool silent, CancellationToken cancellationToken)
    {
        var key = PromptKey(tab.Id, prompt.Id);
        var text = detail == RemoteDetail.JustWhatHappened ? Format.Escape(RemoteMessages.NeedsYou(tab.Name)) : PromptText(tab, prompt);
        var buttons = detail == RemoteDetail.JustWhatHappened
            ? Rows([MakeButton("Show", new ShowButton(tab.Id, prompt.Id))], 1)
            : PromptButtons(tab, prompt);
        var sent = _promptMessages.TryGetValue(key, out var kept) ? kept : _promptMessages[key] = [];
        var reached = 0;
        foreach (var chat in chats)
        {
            try
            {
                // A plan comes as a file first, so its buttons are last in the chat.
                if (detail == RemoteDetail.Everything && prompt is { Kind: RemotePromptKind.Plan, Plan: { Length: > 0 } plan })
                {
                    await SendFileAsync(chat, $"{FileName(tab.Name)} plan.md", plan, $"{Format.Bold(tab.Name)}: the plan", tab.Id, cancellationToken).ConfigureAwait(false);
                }
                var id = await SendAsync(chat, text, buttons, null, silent, tab.Id, cancellationToken).ConfigureAwait(false);
                sent.Add(new PromptCopy(chat, id, text, ++_order));
                reached++;
            }
            catch (ChatApiException ex)
            {
                _logger.LogWarning("{Client}: couldn't send a prompt: {Error}", Name, ex.Message);
            }
        }
        return reached;
    }

    /// <summary>
    /// The prompt waiting in a tab, under what has just said in <paramref name="chat"/> that it needs you (its card, or
    /// its live status): sent there again, silently, and its earlier messages there lose their buttons and say so
    /// (DESIGN.md §18, "What the bot sends").
    /// </summary>
    private async Task AskBelowAsync(long chat, RemoteTab tab, RemotePrompt prompt, CancellationToken cancellationToken)
    {
        var earlier = CopiesIn(chat, tab.Id, prompt.Id).ToList();
        if (await SendPromptAsync([chat], tab, prompt, Desk.Detail, silent: true, cancellationToken).ConfigureAwait(false) == 0)
        {
            // Not sent: the earlier ones keep their buttons.
            return;
        }
        var line = Format.Italic(RemoteMessages.AskedAgain);
        foreach (var copy in earlier)
        {
            _promptMessages[PromptKey(tab.Id, prompt.Id)].Remove(copy);
            await EditAsync(copy.Chat, copy.Message, $"{copy.Text}\n\n{line}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task OnPromptClosedAsync(RemotePromptClosed closed, CancellationToken cancellationToken)
    {
        if (_promptMessages.Remove(PromptKey(closed.TabId, closed.PromptId), out var copies))
        {
            var line = Format.Italic(RemoteMessages.Closed(closed.How, closed.Outcome, closed.Client, closed.At, _time.LocalTimeZone));
            foreach (var copy in copies)
            {
                await EditAsync(copy.Chat, copy.Message, $"{copy.Text}\n\n{line}", null, cancellationToken).ConfigureAwait(false);
            }
        }
        // The tab's live status may still say it needs you, for a prompt asked before that status: it's asked again under it.
        if (_live.TryGetValue(closed.TabId, out var live) && await FindTabAsync(closed.TabId, cancellationToken).ConfigureAwait(false) is { Prompt: { } next } tab
            && CopiesIn(live.Chat, tab.Id, next.Id).ToList() is { Count: > 0 } there && there.All(c => c.Order < live.Order))
        {
            await AskBelowAsync(live.Chat, tab, next, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Turns and errors -------------------------------------------------------------------------------------------

    private async Task OnTurnFinishedAsync(RemoteTurnFinished turn, CancellationToken cancellationToken)
    {
        var notice = Effective(turn.Notice, turn.Tab.Id);
        _engaged.Remove(turn.Tab.Id);
        await FinishLiveAsync(turn.Tab, turn.Took, cancellationToken).ConfigureAwait(false);
        if (notice == RemoteNotice.None)
        {
            return;
        }
        var detail = Desk.Detail;
        var text = detail == RemoteDetail.JustWhatHappened ? Format.Escape(RemoteMessages.Finished(turn.Tab.Name)) : TurnText(turn);
        var buttons = Rows([MakeButton("Whole reply", new ReplyButton(turn.Tab.Id)), MakeButton("Changed files", new FilesButton(turn.Tab.Id))], 2);
        var sent = await BroadcastAsync(text, buttons, notice == RemoteNotice.Silent, turn.Tab.Id, cancellationToken).ConfigureAwait(false);
        // The images it made, when this client sends them, and those Claude only read, when it sends those too.
        var images = detail == RemoteDetail.Everything ? [.. turn.Images.Where(i => i.WasRead ? Desk.SendsReadImages(Kind) : Desk.SendsImages(Kind))] : Array.Empty<RemoteImage>();
        if (images.Length > 0)
        {
            await SendImagesAsync(sent, turn, images, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The images the turn made, after its message in each chat it went to, as a reply to it and silently: the message
    /// made the sound (DESIGN.md §18, "Remote clients", "Images").
    /// </summary>
    private async Task SendImagesAsync(IReadOnlyList<(long Chat, long Message)> sent, RemoteTurnFinished turn, IReadOnlyList<RemoteImage> images, CancellationToken cancellationToken)
    {
        var caption = $"{Format.Bold(turn.Tab.Name)}: {Format.Escape(RemoteMessages.Images(images.Count, turn.ImagesLeftOut, turn.Tab.Agent, images.Count(i => i.WasRead)))}";
        foreach (var (chat, message) in sent)
        {
            try
            {
                var id = await PacedAsync(token => DeliverImagesAsync(chat, images, caption, message, silent: true, token), cancellationToken).ConfigureAwait(false);
                Activity?.Invoke(RemoteActivity.Sent);
                Remember(chat, id, turn.Tab.Id);
            }
            catch (ChatApiException ex)
            {
                _logger.LogWarning("{Client}: couldn't send a turn's images: {Error}", Name, ex.Message);
            }
        }
    }

    private async Task OnTabFailedAsync(RemoteTabFailed failed, CancellationToken cancellationToken)
    {
        var notice = Effective(failed.Notice, failed.Tab.Id);
        _engaged.Remove(failed.Tab.Id);
        await FinishLiveAsync(failed.Tab, null, cancellationToken).ConfigureAwait(false);
        if (notice == RemoteNotice.None)
        {
            return;
        }
        var text = Desk.Detail == RemoteDetail.JustWhatHappened
            ? Format.Escape(RemoteMessages.Stopped(failed.Tab.Name))
            : $"{Header(failed.Tab)}\n{Format.Escape(failed.NeedsSignIn ? RemoteMessages.SignInAtDesk : failed.Message)}";
        var buttons = !failed.NeedsSignIn && failed.Tab.CanRestart && RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Restart)
            ? Rows([MakeButton("Start it again", new RestartButton(failed.Tab.Id))], 1)
            : null;
        await BroadcastAsync(text, buttons, notice == RemoteNotice.Silent, failed.Tab.Id, cancellationToken).ConfigureAwait(false);
    }

    // ---- Live status ------------------------------------------------------------------------------------------------

    /// <summary>A tab's message that shows what it's doing, edited in place at most every 5 seconds, with <b>Stop</b>.</summary>
    private void StartLive(long chat, long message, string heading, RemoteTab tab)
    {
        var live = new Live(chat, message, heading, tab) { Order = ++_order, LastEdit = _time.GetUtcNow(), LastText = LiveText(heading, tab) };
        // The tab's earlier one stays as it last read: the new one carries on.
        _live[tab.Id] = live;
        ScheduleLive(tab.Id, LiveRefreshInterval);
    }

    private void OnTabChanged(RemoteTab tab)
    {
        if (_live.TryGetValue(tab.Id, out var live))
        {
            live.Tab = tab;
            ScheduleLive(tab.Id, live.LastEdit + LiveEditInterval - _time.GetUtcNow());
        }
    }

    private void ScheduleLive(string tabId, TimeSpan delay) => After($"live:{tabId}", delay, token => EditLiveAsync(tabId, token));

    private async Task EditLiveAsync(string tabId, CancellationToken cancellationToken)
    {
        if (!_live.TryGetValue(tabId, out var live))
        {
            return;
        }
        var text = LiveText(live.Heading, live.Tab);
        if (text != live.LastText)
        {
            live.LastText = text;
            live.LastEdit = _time.GetUtcNow();
            await EditAsync(live.Chat, live.Message, text, LiveButtons(live.Tab), cancellationToken).ConfigureAwait(false);
        }
        // While it works, its time moves on.
        if (live.Tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput or RemoteTabStatus.Starting && _live.ContainsKey(tabId))
        {
            ScheduleLive(tabId, LiveRefreshInterval);
        }
    }

    /// <summary>The turn ended: the live message says so, without <b>Stop</b>, and is edited no more.</summary>
    private async Task FinishLiveAsync(RemoteTab tab, TimeSpan? took, CancellationToken cancellationToken)
    {
        if (!_live.Remove(tab.Id, out var live))
        {
            return;
        }
        _timers.Remove($"live:{tab.Id}", out var timer);
        timer?.Dispose();
        var status = tab.Status == RemoteTabStatus.Error ? RemoteMessages.Status(RemoteTabStatus.Error)
            : took is { } time ? $"Finished in {Formats.Duration(time)}"
            : "Finished";
        await EditAsync(live.Chat, live.Message, $"{live.Heading}\n{Format.Escape(status)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A tab picked from <c>/tabs</c>: what it's doing, live, with its buttons; and under it its prompt, when one waits,
    /// asked again if it was sent before.
    /// </summary>
    private async Task SendTabCardAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } tab)
        {
            await SendAsync(chat, Format.Escape(RemoteMessages.TabGone), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var heading = Header(tab);
        var id = await SendAsync(chat, LiveText(heading, tab), CardButtons(tab), null, silent: true, tab.Id, cancellationToken).ConfigureAwait(false);
        StartLive(chat, id, heading, tab);
        if (tab.Prompt is { } prompt)
        {
            await AskBelowAsync(chat, tab, prompt, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EditAsync(long chat, long message, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken)
    {
        try
        {
            await PacedAsync(token => DeliverEditAsync(chat, message, text, buttons, token), cancellationToken).ConfigureAwait(false);
        }
        catch (ChatApiException ex) when (ex.IsMessageGone)
        {
            // The message was deleted in the chat, or is too old to edit: nothing more to say there.
        }
    }
}
