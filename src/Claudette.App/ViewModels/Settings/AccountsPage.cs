using System.Collections.ObjectModel;
using Avalonia.Media;
using Claudette.App.Services;
using Claudette.Core.Accounts;
using Claudette.Core.Credentials;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>One account in Settings → Accounts: its name, tag color, key and spend warnings (DESIGN.md §4, "Accounts").</summary>
public sealed partial class AccountEditor : ObservableObject
{
    private readonly AccountsService _accounts;

    public AccountEditor(AccountsService accounts, ClaudeAccount account)
    {
        _accounts = accounts;
        Account = account;
        Removal = new InlineConfirmation(RemoveAsync);
    }

    public ClaudeAccount Account { get; }

    /// <summary>Renamed as the box loses focus; an empty name keeps the old one.</summary>
    public string Name
    {
        get => Account.Name;
        set
        {
            _accounts.Rename(Account, value);
            OnPropertyChanged();
        }
    }

    public IBrush Brush => new SolidColorBrush(AccountsService.ColorOf(Account));

    /// <summary>The colors a tag can take: the group colors.</summary>
    public static IReadOnlyList<NamedColor> Colors => TabGroupViewModel.Palette;

    public NamedColor? Color
    {
        get => Colors.FirstOrDefault(c => c.Color == AccountsService.ColorOf(Account));
        set
        {
            if (value is not null)
            {
                _accounts.SetColor(Account, value.Color);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <summary>Warn when today's estimated spend passes this many dollars; empty or 0 for no warning.</summary>
    public decimal? DailySpendAlert
    {
        get => Account.DailySpendAlert;
        set
        {
            Account.DailySpendAlert = value is > 0 ? value : null;
            _accounts.SpendAlertsChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Warn when this month's estimated spend passes this many dollars; empty or 0 for no warning.</summary>
    public decimal? MonthlySpendAlert
    {
        get => Account.MonthlySpendAlert;
        set
        {
            Account.MonthlySpendAlert = value is > 0 ? value : null;
            _accounts.SpendAlertsChanged();
            OnPropertyChanged();
        }
    }

    // ---- The key ------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsChangingKey { get; set; }

    [ObservableProperty]
    public partial string NewKey { get; set; } = "";

    [ObservableProperty]
    public partial string? Status { get; set; }

    [RelayCommand]
    private void ChangeKey()
    {
        Status = null;
        IsChangingKey = true;
    }

    [RelayCommand]
    private void CancelKey()
    {
        NewKey = "";
        IsChangingKey = false;
    }

    [RelayCommand]
    private async Task SaveKeyAsync()
    {
        if (NewKey.Trim().Length == 0)
        {
            Status = "Paste the new key first.";
            return;
        }
        try
        {
            await _accounts.SetKeyAsync(Account, NewKey);
            Status = $"Saved the new key in {_accounts.Credentials.Name}. Tabs pick it up within a few minutes, or when they start again.";
            IsChangingKey = false;
        }
        catch (CredentialStoreException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            NewKey = "";
        }
    }

    // ---- Removing it --------------------------------------------------------------------------------------------------

    /// <summary>How many open tabs run under the account: it can't be removed while any do.</summary>
    public int TabsUsing => _accounts.TabsUsing(Account.Id);

    public bool CanRemove => TabsUsing == 0;

    public string RemoveTip => CanRemove
        ? $"Removes {Account.Name} and its key. Folders that used it use Default again."
        : TabsUsing == 1
            ? $"1 open tab runs under {Account.Name}: move it to another account, or close it, first."
            : $"{TabsUsing} open tabs run under {Account.Name}: move them to another account, or close them, first.";

    /// <summary><b>Remove…</b>, which asks first, in place.</summary>
    public InlineConfirmation Removal { get; }

    private async Task RemoveAsync()
    {
        try
        {
            await _accounts.RemoveAsync(Account);
        }
        catch (CredentialStoreException ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>The open tabs changed, which can let it be removed or not.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(TabsUsing));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(RemoveTip));
    }
}

/// <summary>
/// Settings → Accounts (DESIGN.md §4, "Accounts"): the accounts tabs can run under besides Default, adding an API key,
/// and which account a folder opened for the first time uses. Each machine sets up its own: nothing here syncs, and keys
/// go only to the OS credential store.
/// </summary>
public sealed partial class AccountsPage : SettingsPage, IDisposable
{
    public AccountsPage(SettingsContext context) : base(context, SettingsCategory.Accounts)
    {
        Editors = [.. Accounts.All.Select(a => new AccountEditor(Accounts, a))];
        NewFolderChoices = AccountChoice.All(Accounts, Accounts.NewFolderAccount);
        Accounts.Changed += OnAccountsChanged;
    }

    public void Dispose() => Accounts.Changed -= OnAccountsChanged;

    public override IEnumerable<SettingsSearchResult> SearchEntries =>
    [
        Entry("Add an API key account", pageText: "Add an API key"),
        Entry("New folders use"),
        // Each account's own settings, while there are accounts to show them.
        .. HasAccounts
            ? [
                Entry("Warn when today's estimated spend passes"),
                Entry("Warn when this month's estimated spend passes"),
                Entry("Change an API key", pageText: "Change key"),
            ]
            : Array.Empty<SettingsSearchResult>(),
    ];

    private AccountsService Accounts => Services.Accounts;

    public ObservableCollection<AccountEditor> Editors { get; }

    public bool HasAccounts => Editors.Count > 0;

    public string CredentialStoreName => Accounts.Credentials.Name;

    public bool IsCredentialStoreAvailable => Accounts.Credentials.IsAvailable;

    public string? CredentialStoreUnavailableText => Accounts.Credentials.IsAvailable ? null : Accounts.Credentials.UnavailableReason;

    public string KeyNote =>
        $"Each key is kept in {Accounts.Credentials.Name}, never in Claudette's files, and never synced. Claude Code asks Claudette for it when a tab starts, so it's never in a command line or the environment.";

    // ---- Adding an API key --------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial string NewKey { get; set; } = "";

    [ObservableProperty]
    public partial string? AddStatus { get; set; }

    [RelayCommand]
    private async Task AddAsync()
    {
        var name = NewName.Trim();
        if (name.Length == 0 || NewKey.Trim().Length == 0)
        {
            AddStatus = "Give the account a name, and paste its API key.";
            return;
        }
        try
        {
            await Accounts.AddApiKeyAccountAsync(name, NewKey);
            AddStatus = $"Added {name}. Choose it from a folder group's Account menu for that folder's new tabs, or from a tab's to move the tab.";
            NewName = "";
        }
        catch (CredentialStoreException ex)
        {
            AddStatus = ex.Message;
        }
        finally
        {
            NewKey = "";
        }
    }

    // ---- New folders --------------------------------------------------------------------------------------------------

    /// <summary><b>New folders use</b>: Default, or another account.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AccountChoice> NewFolderChoices { get; private set; }

    public AccountChoice NewFolderAccount
    {
        get => NewFolderChoices.FirstOrDefault(c => c.IsChecked) ?? NewFolderChoices[0];
        set
        {
            if (value is not null && value.Id != Accounts.NewFolderAccount)
            {
                Accounts.NewFolderAccount = value.Id;
            }
        }
    }

    /// <summary>The accounts are the user's, like Perforce's per-folder overrides: only <b>New folders use</b> goes back to Default.</summary>
    protected override void ResetSettings() => Accounts.NewFolderAccount = null;

    private void OnAccountsChanged()
    {
        var all = Accounts.All;
        foreach (var gone in Editors.Where(e => !all.Contains(e.Account)).ToList())
        {
            Editors.Remove(gone);
        }
        foreach (var added in all.Where(a => Editors.All(e => e.Account != a)))
        {
            Editors.Add(new AccountEditor(Accounts, added));
        }
        foreach (var editor in Editors)
        {
            editor.Refresh();
        }
        NewFolderChoices = AccountChoice.All(Accounts, Accounts.NewFolderAccount);
        OnPropertyChanged(nameof(NewFolderAccount));
        OnPropertyChanged(nameof(HasAccounts));
    }
}
