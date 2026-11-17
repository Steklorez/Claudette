using System.ComponentModel;
using Deventry.App.Conversation;
using Deventry.Core.Composer;
using Deventry.Core.Protocol;
using Deventry.Core.Remote;
using Deventry.Core.Settings;

namespace Deventry.App.ViewModels;

/// <summary>
/// This tab as the remote clients see and use it (DESIGN.md §18, "Remote clients"): a snapshot of it, its waiting
/// prompts as <see cref="RemotePrompt"/>s and answering them with the card's own commands, a message sent from a client
/// without touching the composer, and what it tells the desk as things happen. On the UI thread.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The client answering a card just now, so its closing says who: "Telegram".</summary>
    private string? _answeringFrom;

    private Services.RemoteClientsService Remote => _services.RemoteClients;

    /// <summary>
    /// <b>Keep from remote clients</b>, in the tab's menu (DESIGN.md §18, "Remote clients", "Keeping a tab from them"):
    /// the clients hear nothing of this tab, its turns, prompts and images, and can't reach it.
    /// </summary>
    public bool KeepsFromRemoteClients => State.KeepFromRemoteClients;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleKeepFromRemoteClients()
    {
        State.KeepFromRemoteClients = !State.KeepFromRemoteClients;
        _services.SaveState();
        OnPropertyChanged(nameof(KeepsFromRemoteClients));
        Remote.OnKeepFromRemoteClientsChanged(this);
    }

    /// <summary>This tab now, for a client; <paramref name="project"/> is its group's name.</summary>
    /// <param name="withPrompt">With the prompt waiting, which reads the conversation: not for what changes as it works.</param>
    internal RemoteTab ToRemote(string project, bool withPrompt = true) => new()
    {
        Id = Id,
        Name = DisplayName,
        Project = project,
        Branch = BranchBadge,
        Agent = AgentShortName,
        Model = ModelName,
        Status = Status switch
        {
            TabStatus.NotStarted => RemoteTabStatus.NotStarted,
            TabStatus.Starting => RemoteTabStatus.Starting,
            TabStatus.Working => RemoteTabStatus.Working,
            TabStatus.NeedsInput => RemoteTabStatus.NeedsInput,
            TabStatus.Unread => RemoteTabStatus.Unread,
            TabStatus.Error => RemoteTabStatus.Error,
            TabStatus.Exited => RemoteTabStatus.Exited,
            _ => RemoteTabStatus.Idle,
        },
        Doing = IsWorking ? Working.Activity : null,
        Since = IsWorking ? Working.StartedAt : null,
        Context = Context.Percent is { } percent ? Math.Clamp(percent / 100, 0, 1) : null,
        Tasks = TodoList.Items.Count > 0 ? new RemoteTaskProgress(TodoList.DoneCount, TodoList.Items.Count) : null,
        Thread = IsThread ? RemoteThreadRole.Thread : IsSubThread ? RemoteThreadRole.SubThread : RemoteThreadRole.None,
        Prompt = withPrompt ? RemotePrompts().FirstOrDefault() : null,
        IsReadOnly = IsReadOnly,
        CanRestart = CanRestart,
    };

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// What's waiting on the user, oldest first, as clients get it: the prompt cards, a thread's messages to approve, and
    /// the cards only the desk can answer.
    /// </summary>
    internal IReadOnlyList<RemotePrompt> RemotePrompts()
    {
        var prompts = new List<RemotePrompt>();
        prompts.AddRange(WaitingPrompts().Select(ToRemotePrompt).OfType<RemotePrompt>());
        prompts.AddRange(ThreadApprovals.Select(ToRemotePrompt));
        foreach (var input in Items.OfType<McpInputItem>().Where(i => i.IsPending))
        {
            prompts.Add(AtDesk(input.Request.RequestId, input.Title, input.Message));
        }
        if (IsAskingTrust)
        {
            prompts.Add(AtDesk("trust", "Trust this folder?", "Its own Claude Code configuration would run here."));
        }
        if (IsAskingOtherAccount)
        {
            prompts.Add(AtDesk("other-account", "Continue under another account?", OtherAccountText));
        }
        if (IsWaitingForSignIn)
        {
            prompts.Add(AtDesk("sign-in", "Claude Code needs you to sign in again.", null));
        }
        return prompts;
    }

    private static RemotePrompt AtDesk(string id, string title, string? about) =>
        new() { Id = id, Kind = RemotePromptKind.AtDesk, Title = title, About = about };

    /// <summary>A prompt card as clients get it; a question in parts gives the first part not answered yet.</summary>
    internal RemotePrompt? ToRemotePrompt(PromptItem item) => item switch
    {
        PermissionItem permission => new RemotePrompt
        {
            Id = permission.Request.RequestId,
            Kind = permission.HasCommand ? RemotePromptKind.Command : permission.HasDiff || permission.Title.StartsWith("Allow editing", StringComparison.Ordinal)
                || permission.Title.StartsWith("Allow creating", StringComparison.Ordinal) ? RemotePromptKind.FileChange : RemotePromptKind.Tool,
            Title = permission.Title,
            About = permission.Command ?? permission.Detail,
            Asker = permission.Asker,
            Options =
            [
                new(RemotePrompt.OptionIds.Allow, "Allow", IsDefault: !permission.Request.DefaultToNo),
                new(RemotePrompt.OptionIds.Deny, "Deny"),
                new(RemotePrompt.OptionIds.DenyWithMessage, "Deny with a message…", TakesText: true),
            ],
        },
        QuestionItem question when NextPart(question) is var (part, entry) => new RemotePrompt
        {
            Id = QuestionPartId(question, part),
            Kind = RemotePromptKind.Question,
            Title = RemoteMessages.QuestionTitle(AgentShortName),
            About = entry.Text,
            Asker = question.Asker,
            Part = question.Questions.Count > 1 ? part + 1 : 0,
            Parts = question.Questions.Count > 1 ? question.Questions.Count : 0,
            Options =
            [
                .. entry.Options.Select((o, i) => new RemoteOption(RemotePrompt.OptionIds.Choice(i), o.Label)),
                new(RemotePrompt.OptionIds.OwnWords, "Answer in my own words", TakesText: true, NeedsText: true),
            ],
        },
        PlanItem plan => new RemotePrompt
        {
            Id = plan.Request.RequestId,
            Kind = RemotePromptKind.Plan,
            Title = RemoteMessages.PlanTitle(AgentShortName),
            Plan = plan.HasPlan ? plan.Plan.ToString() : null,
            Asker = plan.Asker,
            Options =
            [
                .. IsAutoModeAvailable ? [new RemoteOption(RemotePrompt.OptionIds.ApproveAuto, "Approve, auto mode")] : Array.Empty<RemoteOption>(),
                new(RemotePrompt.OptionIds.ApproveAcceptEdits, "Approve, accept edits"),
                new(RemotePrompt.OptionIds.ApproveAskBeforeEdits, "Approve, ask before edits", IsDefault: !plan.Request.DefaultToNo),
                new(RemotePrompt.OptionIds.KeepPlanning, "Keep planning…", TakesText: true),
            ],
        },
        _ => null,
    };

    private RemotePrompt ToRemotePrompt(ThreadSendApproval approval) => new()
    {
        Id = approval.Id,
        Kind = RemotePromptKind.ThreadMessage,
        Title = approval.Title,
        About = approval.Message,
        Recipient = approval.Recipient,
        Options =
        [
            new(RemotePrompt.OptionIds.Send, "Send", IsDefault: true),
            new(RemotePrompt.OptionIds.DontSend, "Don't send"),
        ],
    };

    /// <summary>The first question in <paramref name="item"/> without an answer, and where it is; null when all have one.</summary>
    private static (int Part, QuestionEntry Entry)? NextPart(QuestionItem item)
    {
        for (var i = 0; i < item.Questions.Count; i++)
        {
            if (item.Questions[i].Answer is null)
            {
                return (i, item.Questions[i]);
            }
        }
        return null;
    }

    /// <summary>A question's part has an id of its own, so each part's message closes as it's answered.</summary>
    private static string QuestionPartId(QuestionItem item, int part) =>
        item.Questions.Count > 1 ? $"{item.Request.RequestId}#{part}" : item.Request.RequestId;

    /// <summary>
    /// Answers a waiting prompt from <paramref name="client"/> with one of its options, through the card's own command, so
    /// it closes as one answered here does, saying where it was answered.
    /// </summary>
    internal RemoteResult AnswerRemote(string promptId, string optionId, string? text, string client)
    {
        if (ThreadApprovals.FirstOrDefault(a => a.Id == promptId) is { } approval)
        {
            approval.AnsweredFrom = client;
            switch (optionId)
            {
                case RemotePrompt.OptionIds.Send:
                    approval.SendCommand.Execute(null);
                    return RemoteResult.Ok;
                case RemotePrompt.OptionIds.DontSend:
                    approval.DontSendCommand.Execute(null);
                    return RemoteResult.Ok;
            }
            return RemoteResult.No("That isn't one of its answers.");
        }
        var item = WaitingPrompts().FirstOrDefault(p => p switch
        {
            QuestionItem question => NextPart(question) is var (part, _) && QuestionPartId(question, part) == promptId,
            _ => p.Request.RequestId == promptId,
        });
        if (item is null)
        {
            return RemoteResult.No(RemotePrompts().Any(p => p.Id == promptId) ? RemoteMessages.AtDesk : ClosedReason(promptId));
        }
        _answeringFrom = client;
        try
        {
            return item switch
            {
                PermissionItem permission => AnswerPermission(permission, optionId, text),
                QuestionItem question => AnswerQuestion(question, promptId, optionId, text, client),
                PlanItem plan => AnswerPlan(plan, optionId, text),
                _ => RemoteResult.No(RemoteMessages.AtDesk),
            };
        }
        finally
        {
            _answeringFrom = null;
        }
    }

    /// <summary>How a prompt that isn't waiting any more closed, as far as the tab knows.</summary>
    private string ClosedReason(string promptId) => _closedPrompts.TryGetValue(promptId, out var closed)
        ? RemoteMessages.AlreadyAnswered(closed.How, closed.Client)
        : RemoteMessages.AlreadyAnswered(RemotePromptEnd.Withdrawn, null);

    /// <summary>The prompts closed lately, by id, and how: a client tapping one late is told.</summary>
    private readonly Dictionary<string, (RemotePromptEnd How, string? Client)> _closedPrompts = new(StringComparer.Ordinal);

    private static RemoteResult AnswerPermission(PermissionItem permission, string optionId, string? text)
    {
        switch (optionId)
        {
            case RemotePrompt.OptionIds.Allow:
                permission.AllowCommand.Execute(null);
                break;
            case RemotePrompt.OptionIds.Deny:
                permission.DenyCommand.Execute(null);
                break;
            case RemotePrompt.OptionIds.DenyWithMessage:
                permission.DenyMessage = text ?? "";
                permission.SendDenyMessageCommand.Execute(null);
                break;
            default:
                return RemoteResult.No("That isn't one of its answers.");
        }
        return RemoteResult.Ok;
    }

    private RemoteResult AnswerQuestion(QuestionItem question, string promptId, string optionId, string? text, string client)
    {
        if (NextPart(question) is not var (part, entry))
        {
            return RemoteResult.No(RemoteMessages.AlreadyAnswered(RemotePromptEnd.Withdrawn, null));
        }
        if (RemotePrompt.OptionIds.ChoiceIndex(optionId) is { } index && index < entry.Options.Count)
        {
            // A choice from the phone is the one answer, even to a question that takes several.
            foreach (var option in entry.Options)
            {
                option.IsSelected = false;
            }
            entry.Options[index].IsSelected = true;
        }
        else if (optionId == RemotePrompt.OptionIds.OwnWords && text?.Trim() is { Length: > 0 } words)
        {
            entry.OtherText = words;
        }
        else
        {
            return RemoteResult.No(optionId == RemotePrompt.OptionIds.OwnWords ? "Send the answer in words." : "That isn't one of its answers.");
        }
        if (NextPart(question) is null)
        {
            question.SubmitCommand.Execute(null);
            return RemoteResult.Ok;
        }
        // More parts to answer: this one closes, and the next is asked.
        Remote.OnPromptClosed(this, promptId, RemotePromptEnd.AnsweredFromClient, entry.Answer ?? "", client);
        if (ToRemotePrompt(question) is { } next)
        {
            Remote.OnPromptWaiting(this, next);
        }
        return RemoteResult.Ok;
    }

    private RemoteResult AnswerPlan(PlanItem plan, string optionId, string? text)
    {
        switch (optionId)
        {
            case RemotePrompt.OptionIds.ApproveAuto when IsAutoModeAvailable:
                plan.ApproveInAutoModeCommand.Execute(null);
                break;
            case RemotePrompt.OptionIds.ApproveAcceptEdits:
                plan.ApproveAcceptingEditsCommand.Execute(null);
                break;
            case RemotePrompt.OptionIds.ApproveAskBeforeEdits:
                plan.ApproveAskingForEditsCommand.Execute(null);
                break;
            case RemotePrompt.OptionIds.KeepPlanning:
                plan.Feedback = text ?? "";
                plan.KeepPlanningCommand.Execute(null);
                break;
            default:
                return RemoteResult.No("That isn't one of its answers.");
        }
        return RemoteResult.Ok;
    }

    /// <summary>A prompt card came: when it closes, the clients hear how, and a card answered from one says so.</summary>
    private void WatchForRemote(PromptItem item)
    {
        var ids = item is QuestionItem question ? Enumerable.Range(0, question.Questions.Count).Select(i => QuestionPartId(question, i)).ToArray() : [item.Request.RequestId];
        PropertyChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.PropertyName != nameof(PromptItem.State) || item.IsPending)
            {
                return;
            }
            item.PropertyChanged -= handler;
            var client = _answeringFrom;
            var how = item.State == PermissionState.Cancelled ? RemotePromptEnd.Withdrawn
                : client is not null ? RemotePromptEnd.AnsweredFromClient
                : RemotePromptEnd.AnsweredAtDesk;
            var outcome = item.Outcome;
            foreach (var id in ids)
            {
                Remote.OnPromptClosed(this, id, how, outcome, client);
            }
            if (client is not null)
            {
                item.Outcome = $"{outcome} · {RemoteMessages.AnsweredFrom(client)}";
            }
        };
        item.PropertyChanged += handler;
    }

    /// <summary>A thread's message to approve closed: sent, not sent, or not answered in time.</summary>
    private void OnApprovalResolvedForRemote(ThreadSendApproval approval)
    {
        var answered = approval.Answer.Task.IsCompletedSuccessfully;
        var how = !answered ? RemotePromptEnd.Withdrawn : approval.AnsweredFrom is not null ? RemotePromptEnd.AnsweredFromClient : RemotePromptEnd.AnsweredAtDesk;
        var outcome = !answered ? RemoteMessages.WithdrawnOutcome : approval.Answer.Task.Result ? "Sent" : "Not sent";
        Remote.OnPromptClosed(this, approval.Id, how, outcome, approval.AnsweredFrom);
    }

    /// <summary>Remembers how a prompt closed, for a client that taps it late; called by the service as it tells the clients.</summary>
    internal void RememberClosed(string promptId, RemotePromptEnd how, string? client)
    {
        _closedPrompts[promptId] = (how, client);
        if (_closedPrompts.Count > 200)
        {
            _closedPrompts.Remove(_closedPrompts.Keys.First());
        }
    }

    // ---- Messages, stopping and starting again ----------------------------------------------------------------------

    /// <summary>
    /// A message from <paramref name="client"/>, sent as typing it here would be: the library check, held and queued
    /// messages, mentions and the thread's note; but the composer, its draft and its quick suffixes stay as they are.
    /// Its card says it came from the client.
    /// </summary>
    /// <param name="attachments">
    /// Images and files sent with it (DESIGN.md §18, "Attachments from a client"): images go in the message, as the
    /// composer's do; a text file in it, as a large paste; anything else Claude reads is saved in the project's
    /// <c>.deventry/attachments</c> and mentioned.
    /// </param>
    internal async Task<RemoteResult> SendRemoteAsync(string text, IReadOnlyList<RemoteAttachment> attachments, string client)
    {
        var message = text.Trim();
        if (message.Length == 0 && attachments.Count == 0)
        {
            return RemoteResult.No("There's nothing to send.");
        }
        if (IsReadOnly)
        {
            return RemoteResult.No($"{DisplayName} continued on {TakenOverBy}: it can only be read here.");
        }
        if (Status == TabStatus.Error && !IsWaitingForSignIn)
        {
            return RemoteResult.No($"{DisplayName} stopped with an error. Start it again first.");
        }
        if (Status == TabStatus.Starting)
        {
            return RemoteResult.No($"{DisplayName} is still starting. Try again in a moment.");
        }
        if (await CheckLibraryBeforeSendingAsync(client))
        {
            return RemoteResult.No($"Another machine's turns just reached {DisplayName}, so the message wasn't sent. Read them, then send it again.");
        }
        var prepared = PreparedAttachments.None;
        if (attachments.Count > 0)
        {
            var folder = Folder;
            var now = _services.Time.GetLocalNow();
            var keep = _services.Settings.RemoteClients.KeepAttachments.ToTimeSpan();
            try
            {
                prepared = await Task.Run(() => RemoteAttachments.Prepare(attachments, folder, now, keep));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RemoteResult.No($"Couldn't save the files in {Path.Combine(folder, RemoteAttachments.DeventryFolder)}: {ex.Message}");
            }
            if (prepared.IsEmpty && message.Length == 0)
            {
                return RemoteResult.No(string.Join("\n", prepared.Refused));
            }
        }
        // A saved file is mentioned after the words, as a dropped one is, for Claude Code to read.
        var sent = prepared.Mentions.Count == 0 ? message : $"{message}{(message.Length > 0 ? "\n\n" : "")}{string.Join(" ", prepared.Mentions)}";
        // A text file goes after them as a large paste does, shown on the card as one.
        await SendTypedAsync(PastedText.Join(sent, prepared.Pastes), sent, null, prepared.Images, prepared.Pastes, via: client);
        return IsAskingTrust ? RemoteResult.No("Sent, but this folder asks whether to trust it first: answer that at the desk.")
            : IsAskingOtherAccount ? RemoteResult.No("Sent, but it waits to be told at the desk whether to continue under another account.")
            : prepared.Refused.Count > 0 ? new RemoteResult(true, $"Not sent: {string.Join(" ", prepared.Refused)}")
            : RemoteResult.Ok;
    }

    /// <summary><b>Stop</b>, from a client: what was waiting its turn goes back to the composer, as it does here.</summary>
    internal async Task<RemoteResult> StopRemoteAsync()
    {
        if (!IsWorking)
        {
            return RemoteResult.No($"{DisplayName} isn't working.");
        }
        await StopAsync();
        return RemoteResult.Ok;
    }

    /// <summary>Starts a tab that stopped or exited again, from a client.</summary>
    internal RemoteResult RestartRemote()
    {
        if (!CanRestart)
        {
            return RemoteResult.No(IsReadOnly ? $"{DisplayName} can only be read here." : $"{DisplayName} can't be started again from here.");
        }
        _ = RestartAsync();
        return RemoteResult.Ok;
    }

    /// <summary>The last reply, as Markdown; null when there's none yet.</summary>
    internal RemoteReply? LastRemoteReply() =>
        Items.OfType<AssistantTextItem>().LastOrDefault() is { } reply && reply.Markdown.ToString().Trim() is { Length: > 0 } text
            ? new RemoteReply(Id, DisplayName, text, reply.SentAt)
            : null;

    /// <summary>The files the session changed, as the Changed files window lists them.</summary>
    internal IReadOnlyList<RemoteFileChange> RemoteChangedFiles() =>
        [.. ChangedFiles.Files.Select(f => new RemoteFileChange(f.DisplayPath, f.Added ?? 0, f.Removed ?? 0, f.IsAdded, f.IsDeleted))];

    // ---- What the desk is told --------------------------------------------------------------------------------------

    /// <summary>
    /// A turn ended: once its files are on its footer, they're counted against how it left them, off the UI thread, and
    /// the clients hear of it with the reply.
    /// </summary>
    private void TellRemoteTurnFinished(ResultMessage result)
    {
        if (!Remote.IsActive || KeepsFromRemoteClients)
        {
            return;
        }
        var took = result.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        var tasks = _conversation.TurnTasksSummary?.Text;
        var reply = result.Result?.Trim() is { Length: > 0 } text ? text : LastRemoteReply()?.Markdown;
        // After this turn's handler has put the files on its footer.
        _services.Dispatcher.Post(async () =>
        {
            var rows = Items.LastOrDefault() as TurnSummaryItem is { } summary ? summary.Files : [];
            IReadOnlyList<RemoteFileChange> files;
            try
            {
                files = await ChangedFiles.CountForRemoteAsync(rows);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The files went, or can't be read: they're named without their lines.
                files = [.. rows.Select(r => new RemoteFileChange(r.DisplayPath, 0, 0, r.Change.IsNew))];
            }
            var (images, leftOut) = Remote.WantsImages ? await TurnImagesAsync() : ([], 0);
            Remote.OnTurnFinished(this, took, tasks, reply, files, images, leftOut);
        });
    }

    // ---- The images a turn made (DESIGN.md §18, "Remote clients", "Images") ---------------------------------------

    /// <summary>How long a turn's end waits for the files its reply names to be looked for, before the clients hear of it without them.</summary>
    internal static readonly TimeSpan TurnImagesWait = TimeSpan.FromSeconds(5);

    /// <summary>A file written this long before the turn started counts as the turn's: file systems keep times coarsely.</summary>
    private static readonly TimeSpan WriteTimeSlack = TimeSpan.FromSeconds(2);

    /// <summary>When this turn started: an image file written since was made by it.</summary>
    private DateTimeOffset _turnStartedAt;

    /// <summary>The agent's images the gallery has had since this turn started, each once.</summary>
    private readonly List<MediaEntry> _turnImages = [];

    /// <summary>A turn started: the images it makes are counted from now.</summary>
    private void StartTurnImages()
    {
        _turnStartedAt = _services.Time.GetUtcNow();
        _turnImages.Clear();
    }

    /// <summary>The gallery has an image of the agent's, seen live: one this turn may have made.</summary>
    private void OnAgentImageSeen(MediaEntry entry)
    {
        _turnImages.RemoveAll(e => e.Key == entry.Key);
        _turnImages.Add(entry);
    }

    /// <summary>
    /// The images the turn made, once what its reply names has been looked for (but not past
    /// <see cref="TurnImagesWait"/>): read and drawn for sending off the UI thread.
    /// </summary>
    private async Task<(IReadOnlyList<RemoteImage> Images, int LeftOut)> TurnImagesAsync()
    {
        using (var waited = new CancellationTokenSource())
        {
            await Task.WhenAny(Media.SettledAsync(), Task.Delay(TurnImagesWait, _services.Time, waited.Token));
            await waited.CancelAsync();
        }
        // In the order the conversation last showed them, as the Media window has them, however long finding a file took.
        var seen = _turnImages.OrderBy(e => e.Order).ToArray();
        var since = _turnStartedAt - WriteTimeSlack;
        var withRead = Remote.WantsReadImages;
        return seen.Length == 0 ? ([], 0) : await Task.Run(() => Services.RemoteImageFiles.Made(seen, since, withRead));
    }
}
