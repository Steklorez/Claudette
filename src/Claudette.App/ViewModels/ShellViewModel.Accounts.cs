using Claudette.Core.Accounts;

namespace Claudette.App.ViewModels;

/// <summary>
/// The accounts in the sidebar (DESIGN.md §4, "Accounts"): which one each group's new tabs use, and keeping the groups'
/// and tabs' tags and <b>Account ▸</b> menus current as accounts come and go.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The selected tab's account, which the header follows; null for Default.</summary>
    public ClaudeAccount? SelectedAccount => SelectedTab?.Account;

    /// <summary>A tab moved to another account: the header follows if it's the selected one.</summary>
    internal void OnTabAccountChanged(TabViewModel tab)
    {
        if (tab == SelectedTab)
        {
            OnPropertyChanged(nameof(SelectedAccount));
        }
    }

    /// <summary>A new group: its account and its menu.</summary>
    private void SetUpGroupAccount(TabGroupViewModel group)
    {
        group.AccountChosen = SetGroupAccount;
        RefreshGroupAccount(group);
    }

    private void RefreshGroupAccount(TabGroupViewModel group)
    {
        var accounts = _services.Accounts;
        group.Account = accounts.Find(accounts.ForFolder(group.Folder));
        group.AccountMenu = accounts.HasAccounts ? AccountMenuItem.All(accounts, group.Account?.Id, group.ChooseAccountCommand) : [];
    }

    /// <summary>
    /// <b>Account ▸</b> in a group's menu: the group's new tabs use the account, remembered for its folder. The tabs it
    /// has keep theirs; each has its own <b>Account ▸</b>.
    /// </summary>
    public void SetGroupAccount(TabGroupViewModel group, string? accountId) => _services.Accounts.SetForFolder(group.Folder, accountId);

    private void OnAccountsChanged()
    {
        foreach (var group in Groups)
        {
            RefreshGroupAccount(group);
        }
        foreach (var tab in AllTabs)
        {
            tab.AccountChanged();
        }
    }
}
