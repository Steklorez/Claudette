using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.Core.Accounts;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The account this tab runs Claude Code under (DESIGN.md §4, "Accounts"): the tag on its row while it isn't its group's,
/// the info card's row, and <b>Account ▸</b>, which starts Claude Code again under another account and carries the
/// conversation on there.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The account changed while Claude worked: Claude Code starts again under it once the turn ends.</summary>
    private bool _restartForAccount;

    /// <summary>The account the tab runs under; null for Default, and for one removed since.</summary>
    public ClaudeAccount? Account => _services.Accounts.Find(State.AccountId);

    public string AccountName => _services.Accounts.NameOf(State.AccountId);

    /// <summary>The tag on the tab's row, while its account isn't the one its group's new tabs use.</summary>
    public AccountTag? RowAccountTag => Account?.Id == _services.Accounts.ForFolder(GroupFolder) ? null : AccountTag.For(Account, showDefault: true);

    public bool ShowsAccountTag => RowAccountTag is not null;

    /// <summary><b>Account ▸</b> in the tab's menu: Default and every other account, the tab's own checked.</summary>
    public IReadOnlyList<AccountMenuItem> AccountMenu => AccountMenuItem.All(_services.Accounts, Account?.Id, SwitchAccountCommand);

    /// <summary>The menu is there once there's an account besides Default.</summary>
    public bool HasAccountMenu => _services.Accounts.HasAccounts;

    /// <summary>Why the Claude app isn't for this tab's account whatever the Default account can do: an API key can't.</summary>
    internal string? AccountRemoteControlReason => Account is { } account
        ? $"{account.Name} uses an API key, and the Claude app needs a claude.ai sign-in"
        : null;

    /// <summary>
    /// Picks the tab's account. With a conversation to carry on it asks first, saying what the move costs: the prompt
    /// cache isn't shared between accounts, so Claude reads the whole conversation again once.
    /// </summary>
    [RelayCommand]
    private void SwitchAccount(string? accountId)
    {
        var target = _services.Accounts.Find(accountId);
        if (target?.Id == Account?.Id)
        {
            // The menu item ticked itself as it was clicked: reading the list again puts it back.
            OnPropertyChanged(nameof(AccountMenu));
            return;
        }
        if (State.SessionId is null)
        {
            _ = ApplyAccountAsync(target);
            return;
        }
        var name = target?.Name ?? AccountsService.DefaultName;
        _shell.Confirm($"Move \"{DisplayName}\" to {name}?",
            SwitchCost.Describe(name, Context.Tokens ?? 0, _modelId, billedByToken: target is not null),
            "Move", () => ApplyAccountAsync(target));
    }

    /// <summary>
    /// Runs the tab under <paramref name="account"/> from now on. A running Claude Code starts again under it now, keeping
    /// the conversation; one in the middle of a turn, once the turn ends.
    /// </summary>
    private async Task ApplyAccountAsync(ClaudeAccount? account)
    {
        State.AccountId = account?.Id;
        _services.SaveState();
        AccountChanged();
        if (_session is null)
        {
            return;
        }
        if (IsWorking)
        {
            _restartForAccount = true;
            _conversation.AddNote($"Moves to {AccountName} once this turn ends, when Claude Code starts again.");
            return;
        }
        await RestartForAccountAsync();
    }

    /// <param name="afterTurn">The turn whose end asked for it: a turn started since then is running.</param>
    private async Task RestartForAccountAsync(int? afterTurn = null)
    {
        // A message sent while Claude worked runs next: a restart now would take it with the session.
        if (afterTurn is { } turn && turn != _turnsStarted || Items.OfType<UserMessageItem>().Any(m => m.IsQueued))
        {
            _restartForAccount = true;
            return;
        }
        _restartForAccount = false;
        await StopSessionAsync();
        // Accounts share no prompt cache: the conversation is read again in full (DESIGN.md §6, "Prompt cache").
        ResetPromptCache();
        _conversation.AddNote($"Started Claude Code again under {AccountName}.");
        Status = TabStatus.NotStarted;
        await EnsureStartedAsync();
    }

    /// <summary>The tab's account, its group's, or the accounts themselves changed.</summary>
    internal void AccountChanged()
    {
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(RowAccountTag));
        OnPropertyChanged(nameof(ShowsAccountTag));
        OnPropertyChanged(nameof(AccountMenu));
        OnPropertyChanged(nameof(HasAccountMenu));
        OnPropertyChanged(nameof(HasUsageCreditsMenu));
        OnPropertyChanged(nameof(InfoRows));
        RemoteControl.OnAvailabilityChanged();
        _shell.OnTabAccountChanged(this);
        RefreshPromptCache();
    }

    /// <summary>The info card's "Account" row, once there's an account besides Default.</summary>
    private void AddAccountInfoRows(List<InfoRow> rows)
    {
        if (_services.Accounts.HasAccounts)
        {
            rows.Add(new InfoRow("Account", Account is { } account ? $"{account.Name} (API key)" : AccountsService.DefaultName));
        }
    }

    /// <summary>
    /// Claude Code turned the tab's API key down, or couldn't get it. That's this account's to fix, not a sign-in to
    /// Claude Code, so the other tabs and the Default account carry on.
    /// </summary>
    private void OnApiKeyRefused(ClaudeAccount account, string? said)
    {
        Status = TabStatus.Error;
        _conversation.AddNote(
            $"Claude Code couldn't use {account.Name}'s API key{(said is { Length: > 0 } ? $" (it said \"{said}\")" : "")}. Check the key in Settings → Accounts, or move the tab to another account.",
            NoteKind.Error);
    }
}
