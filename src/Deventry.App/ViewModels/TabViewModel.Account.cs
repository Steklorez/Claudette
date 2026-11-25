using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Accounts;
using Deventry.Core.Sessions;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The account this tab runs Claude Code under (DESIGN.md §4, "Accounts"): the tag on its row while it isn't its group's,
/// the info card's row, and <b>Account ▸</b>, which starts Claude Code again under another account and carries the
/// conversation on there.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The account changed while Claude worked: Claude Code starts again under it once the turn ends.</summary>
    private bool _restartForAccount;

    /// <summary>
    /// The account the tab runs under; null for Default, and for one removed since. Accounts are Claude Code's: a Codex
    /// tab runs under Codex's own sign-in (DESIGN.md §18, "Codex, a second agent").
    /// </summary>
    public ClaudeAccount? Account => IsCodex ? null : _services.Accounts.Find(State.AccountId);

    public string AccountName => IsCodex ? "Codex's own sign-in" : _services.Accounts.NameOf(State.AccountId);

    /// <summary>The tag on the tab's row, while its account isn't the one its group's new tabs use; none on a Codex tab.</summary>
    public AccountTag? RowAccountTag => IsCodex || Account?.Id == _services.Accounts.ForFolder(GroupFolder) ? null : AccountTag.For(Account, showDefault: true);

    public bool ShowsAccountTag => RowAccountTag is not null;

    /// <summary><b>Account ▸</b> in the tab's menu: Default and every other account, the tab's own checked.</summary>
    public IReadOnlyList<AccountMenuItem> AccountMenu => AccountMenuItem.All(_services.Accounts, Account?.Id, SwitchAccountCommand);

    /// <summary>The menu is there once there's an account besides Default, on a Claude Code tab.</summary>
    public bool HasAccountMenu => !IsCodex && _services.Accounts.HasAccounts;

    /// <summary>
    /// Why the Claude app isn't for this tab's account whatever the Default account can do: an API key or a cloud
    /// provider can't.
    /// </summary>
    internal string? AccountRemoteControlReason => Account is { } account
        ? $"{account.Name} uses {(account.Provider?.Name ?? account.Server?.Name ?? "an API key")}, and the Claude app needs a claude.ai sign-in"
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
        var tokens = Context.Tokens ?? 0;
        if (target is { Server: { } server, Local: { } local })
        {
            // An Ollama or llama.cpp model reads it for nothing, if it fits its window (DESIGN.md §18).
            if (!SwitchCost.Fits(local, tokens))
            {
                _shell.Confirm($"Move \"{DisplayName}\" to {name}?", SwitchCost.DoesntFit(name, local, tokens),
                    "New chat with a digest", () => NewChatWithDigestUnderAsync(target));
                return;
            }
            _shell.Confirm($"Move \"{DisplayName}\" to {name}?", SwitchCost.DescribeLocal(server, local, tokens), "Move", () => ApplyAccountAsync(target));
            return;
        }
        var billing = target is null ? ClaudeBilling.Subscription : target.IsCloudProvider ? ClaudeBilling.CloudProvider : ClaudeBilling.ApiKey;
        _shell.Confirm($"Move \"{DisplayName}\" to {name}?",
            SwitchCost.Describe(name, tokens, _modelId, billing, target?.Provider?.Name),
            "Move", () => ApplyAccountAsync(target));
    }

    /// <summary>
    /// A conversation too big for an Ollama or llama.cpp account's model: the tab starts a new chat under it, with a
    /// digest of this one in the composer to trim and send, as continuing under another account offers (DESIGN.md §4).
    /// </summary>
    private async Task NewChatWithDigestUnderAsync(ClaudeAccount account)
    {
        var digest = ConversationDigest(Items);
        State.AccountId = account.Id;
        ConfirmCurrentAccount();
        _services.SaveState();
        AccountChanged();
        await StartNewChatAsync(account.Name);
        if (digest.Length > 0)
        {
            ComposerText = ComposerText.Trim() is { Length: > 0 } held ? $"{digest}\n\n{held}" : digest;
        }
    }

    /// <summary>
    /// Runs the tab under <paramref name="account"/> from now on. A running Claude Code starts again under it now, keeping
    /// the conversation; one in the middle of a turn, once the turn ends.
    /// </summary>
    private async Task ApplyAccountAsync(ClaudeAccount? account)
    {
        State.AccountId = account?.Id;
        // Moving it was the user's choice, so resuming the session doesn't ask about the account (DESIGN.md §4).
        ConfirmCurrentAccount();
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
            rows.Add(new InfoRow("Account", Account is { } account ? $"{account.Name} ({account.KindName})" : AccountsService.DefaultName));
        }
    }

    /// <summary>
    /// Claude Code turned the tab's API key or cloud credentials down, or couldn't get them. That's this account's to
    /// fix, not a sign-in to Claude Code, so the other tabs and the Default account carry on.
    /// </summary>
    private void OnApiKeyRefused(ClaudeAccount account, string? said)
    {
        Status = TabStatus.Error;
        var saying = said is { Length: > 0 } ? $" (it said \"{said}\")" : "";
        _conversation.AddNote(account.Provider is { } provider
            ? $"Claude Code couldn't sign in to {provider.Name} for {account.Name}{saying}. {CredentialsHint(account)} Or move the tab to another account."
            : $"Claude Code couldn't use {account.Name}'s API key{saying}. Check the key in Settings → Claude Code → Accounts, or move the tab to another account.",
            NoteKind.Error);
    }

    /// <summary>Where to look when a cloud account's credentials are refused, for how it signs in.</summary>
    private static string CredentialsHint(ClaudeAccount account) => account.Cloud?.SignIn switch
    {
        CloudSignIns.Profile when account.Cloud.Profile is { Length: > 0 } profile => $"Sign in to AWS with \"aws sso login --profile {profile}\", or check the profile in Settings → Claude Code → Accounts.",
        CloudSignIns.Profile => "Sign in to AWS, or check its credentials in ~/.aws.",
        CloudSignIns.Gcloud => $"Sign in with \"{AccountLaunch.GcloudSignInCommand}\", or check the project in Settings → Claude Code → Accounts.",
        CloudSignIns.EntraId => "Sign in with \"az login\", or check the resource name in Settings → Claude Code → Accounts.",
        CloudSignIns.KeyFile => "Check the key file in Settings → Claude Code → Accounts.",
        _ => "Check the key in Settings → Claude Code → Accounts.",
    };

    /// <summary>
    /// Claude Code says which provider the session started on: when it isn't the account's, such as when an
    /// organization's managed settings choose one, the tab says so rather than billing it quietly.
    /// </summary>
    private void CheckAccountProvider(string? apiProvider)
    {
        if (Account is { } account && apiProvider is { Length: > 0 } && !CloudProviders.IsExpected(account, apiProvider))
        {
            _conversation.AddNote(
                $"Claude Code is using {CloudProviders.NameOf(apiProvider)} for this tab, not {(account.Provider?.Name ?? "an API key")} as {account.Name} says. An organization's managed settings may choose it.",
                NoteKind.Warning);
        }
    }

    /// <summary>
    /// A setting in one of Claude Code's settings files takes over from the key the account put in the environment of
    /// the tab's <c>claude</c>: the tab says which, since Deventry can't put the key in the settings instead.
    /// </summary>
    private async Task NoteKeyTakenOverAsync(string keyVariable, string? folder)
    {
        var file = await Task.Run(() => AccountLaunch.SettingsFileSetting(_services.ClaudeSettingsFilesFor(folder), keyVariable));
        if (file is not null && Account is { } account)
        {
            _conversation.AddNote(
                $"{file} sets {keyVariable}, which Claude Code uses over {account.Name}'s key. Remove it there for this account's key to be used.",
                NoteKind.Warning);
        }
    }
}
