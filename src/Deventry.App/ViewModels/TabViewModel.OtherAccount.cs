using Deventry.App.Conversation;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

public partial class TabViewModel
{
    // ---- Continuing under another account (DESIGN.md §4, "Continuing under another account") ----------------

    /// <summary>The account the session was started under, while the tab asks about carrying on under another.</summary>
    private SessionOrigin? _askedOrigin;

    private SessionOrigin? _askedCurrent;

    /// <summary>What the tab says while it asks. Null when it isn't asking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingOtherAccount))]
    public partial string? OtherAccountText { get; private set; }

    public bool IsAskingOtherAccount => OtherAccountText is not null;

    /// <summary><b>Use X instead</b>: where Deventry can put the tab back on the account the session started under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseOriginAccount))]
    public partial string? UseOriginAccountText { get; private set; }

    public bool CanUseOriginAccount => UseOriginAccountText is not null;

    /// <summary>The account the tab would run under now, when Deventry can tell which: an API key's, or the signed-in Default.</summary>
    private SessionOrigin? CurrentOrigin()
    {
        if (Account is { } account)
        {
            return new SessionOrigin { AccountId = account.Id, Name = account.Name };
        }
        return _services.RemoteControl.Account is { Email: { Length: > 0 } email } signedIn
            ? new SessionOrigin { Email = email, Organization = signedIn.OrganizationName ?? "" }
            : null;
    }

    /// <summary>
    /// Before a session resumes: when it was started under another account than the one the tab runs under now, and the
    /// user hasn't said to go on, the tab asks instead of starting, since the earlier conversation goes to that account.
    /// True when it asks.
    /// </summary>
    private bool AskOtherAccount(string? sessionId)
    {
        if (sessionId is null || !_services.Settings.Accounts.WarnBeforeOtherAccount
            || !_services.State.SessionOrigins.TryGetValue(sessionId, out var origin) || CurrentOrigin() is not { } current
            || origin.IsSameAs(current) || State.ConfirmedOtherAccount == $"{sessionId}|{current.Key}")
        {
            return false;
        }
        _askedOrigin = origin;
        _askedCurrent = current;
        var from = origin.Describe(_services.Accounts.Find(origin.AccountId)?.Name);
        var to = current.Describe();
        OtherAccountText = $"This conversation was started under {from}. It will continue under {to}. Claude Code sends the whole earlier "
            + $"conversation with its requests, so the organization of {to} can see it, and Claude can't use its earlier thinking from another "
            + "organization: its next replies may be slower and use more tokens.";
        // Only another of Deventry's accounts can be put back; a different sign-in is Claude Code's to change.
        UseOriginAccountText = origin.AccountId != State.AccountId && (origin.AccountId is null || _services.Accounts.Find(origin.AccountId) is not null)
            ? $"Use {(origin.AccountId is null ? "the Default account" : _services.Accounts.NameOf(origin.AccountId))} instead"
            : null;
        Status = TabStatus.NeedsInput;
        return true;
    }

    private void CloseOtherAccountCard()
    {
        OtherAccountText = null;
        UseOriginAccountText = null;
        _askedOrigin = null;
        _askedCurrent = null;
    }

    /// <summary><b>Continue here</b>: the session starts under the new account, and the tab doesn't ask again for it.</summary>
    [RelayCommand]
    private async Task ContinueUnderOtherAccountAsync()
    {
        if (_askedCurrent is { } current && State.SessionId is { } sessionId)
        {
            State.ConfirmedOtherAccount = $"{sessionId}|{current.Key}";
            _services.SaveState();
        }
        CloseOtherAccountCard();
        Status = TabStatus.NotStarted;
        await StartAndSendHeldAsync();
    }

    /// <summary>
    /// The user moved the tab to another account with <b>Account ▸</b>, which asked first: the session carries on there
    /// without this question too.
    /// </summary>
    private void ConfirmCurrentAccount()
    {
        if (State.SessionId is { } sessionId && CurrentOrigin() is { } current)
        {
            State.ConfirmedOtherAccount = $"{sessionId}|{current.Key}";
        }
    }

    /// <summary>
    /// <b>Cancel</b>: nothing is sent and the tab stays as it was, not started, with a message sent while it asked back
    /// in the composer; sending a message asks again.
    /// </summary>
    [RelayCommand]
    private void CancelOtherAccount()
    {
        CloseOtherAccountCard();
        TakeBackHeldForAccount();
        Status = TabStatus.NotStarted;
    }

    /// <summary>The messages sent while the tab asked go back to the composer, and their cards go: none was sent.</summary>
    private void TakeBackHeldForAccount()
    {
        var ids = _heldForTrust.Select(m => m.Stamp?.Uuid).OfType<string>().ToList();
        _heldForTrust.Clear();
        TakeBack(ids);
    }

    /// <summary><b>Use X instead</b>: the tab moves to the account the session started under, and starts.</summary>
    [RelayCommand]
    private async Task UseOriginAccountAsync()
    {
        var origin = _askedOrigin;
        CloseOtherAccountCard();
        if (origin is null)
        {
            return;
        }
        await ApplyAccountAsync(_services.Accounts.Find(origin.AccountId));
        Status = TabStatus.NotStarted;
        await StartAndSendHeldAsync();
    }

    /// <summary>
    /// <b>New chat</b>: nothing of the earlier conversation goes to the new account. The tab starts a new session, as
    /// going back to before the first prompt does, and the earlier conversation stays in History; a message sent while
    /// it asked goes back to the composer, written for a conversation the new chat doesn't have.
    /// </summary>
    [RelayCommand]
    private async Task NewChatUnderOtherAccountAsync()
    {
        var to = _askedCurrent?.Describe();
        CloseOtherAccountCard();
        TakeBackHeldForAccount();
        await StartNewChatAsync(to);
    }

    /// <summary>
    /// <b>New chat with a digest</b>: a new session, and the composer holds a short digest of the earlier conversation,
    /// made here from its cards, with the held message after it, for the user to trim before sending: only what they
    /// send goes to the new account.
    /// </summary>
    [RelayCommand]
    private async Task NewChatWithDigestAsync()
    {
        var to = _askedCurrent?.Describe();
        var digest = ConversationDigest(Items);
        CloseOtherAccountCard();
        TakeBackHeldForAccount();
        await StartNewChatAsync(to);
        if (digest.Length > 0)
        {
            ComposerText = ComposerText.Trim() is { Length: > 0 } held ? $"{digest}\n\n{held}" : digest;
        }
    }

    private Task StartNewChatAsync(string? account) =>
        RestartAtAsync(null, null, $"A new chat{(account is null ? "" : $" under {account}")}. The earlier conversation is in History.");

    /// <summary>The longest digest <see cref="NewChatWithDigestCommand"/> makes, and the most of one message it keeps.</summary>
    private const int DigestLimit = 6000, DigestMessageLimit = 600;

    /// <summary>
    /// The user's prompts and Claude's answers, each cut short, newest kept when it's too long; tool calls and thinking
    /// are left out. Empty when there's nothing to keep.
    /// </summary>
    internal static string ConversationDigest(IEnumerable<ConversationItem> items)
    {
        static string Cut(string text) => (text = text.Trim()).Length <= DigestMessageLimit ? text : text[..DigestMessageLimit].TrimEnd() + "…";
        var lines = items.Select(item => item switch
            {
                UserMessageItem { IsCheckIn: false, IsAutoContinue: false } user when user.Text.Trim().Length > 0 => $"Me: {Cut(user.Text)}",
                AssistantTextItem { IsIntermediate: false } answer when answer.Text.Trim().Length > 0 => $"Claude: {Cut(answer.Text)}",
                _ => null,
            })
            .OfType<string>()
            .ToList();
        var kept = new List<string>();
        var length = 0;
        for (var i = lines.Count - 1; i >= 0 && length + lines[i].Length <= DigestLimit; i--)
        {
            kept.Insert(0, lines[i]);
            length += lines[i].Length + 2;
        }
        return kept.Count == 0 ? "" : "Context from an earlier conversation (a digest):\n\n" + string.Join("\n\n", kept);
    }

    /// <summary>
    /// A session began or took a new id: it starts under the account the tab runs under, when that's known. A copy of
    /// <paramref name="previousId"/> has its origin, and what the user confirmed for it.
    /// </summary>
    private void RecordSessionOrigin(string? previousId, string sessionId, bool isCopy)
    {
        var origins = _services.State.SessionOrigins;
        if (origins.ContainsKey(sessionId))
        {
            return;
        }
        var now = _services.Time.GetUtcNow();
        if (isCopy && previousId is not null)
        {
            if (origins.TryGetValue(previousId, out var source))
            {
                origins[sessionId] = source.For(now);
            }
            if (State.ConfirmedOtherAccount is { } confirmed && confirmed.StartsWith($"{previousId}|", StringComparison.Ordinal))
            {
                State.ConfirmedOtherAccount = $"{sessionId}|{confirmed[(previousId.Length + 1)..]}";
            }
        }
        else if (CurrentOrigin() is { } current)
        {
            origins[sessionId] = current.For(now);
        }
        SessionOrigin.KeepNewest(origins);
        _services.SaveState();
    }
}
