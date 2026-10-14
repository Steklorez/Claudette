using Avalonia.Media;
using Claudette.App.Services;
using Claudette.Core.Accounts;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// An account in a menu or a list that picks one (DESIGN.md §4, "Accounts"): a group's and a tab's <b>Account ▸</b>, and
/// Settings → Accounts' <b>New folders use</b>.
/// </summary>
/// <param name="Id">The account's id; null for Default.</param>
/// <param name="IsChecked">It's the one chosen now.</param>
public sealed record AccountChoice(string? Id, string Name, bool IsChecked, IBrush? Brush)
{
    public bool IsDefault => Id is null;

    public override string ToString() => Name;

    /// <summary>Default, then every other account, with <paramref name="chosen"/> checked.</summary>
    public static IReadOnlyList<AccountChoice> All(AccountsService accounts, string? chosen) =>
    [
        new(null, AccountsService.DefaultName, chosen is null, null),
        .. accounts.All.Select(a => new AccountChoice(a.Id, a.Name, a.Id == chosen, new SolidColorBrush(AccountsService.ColorOf(a)))),
    ];
}

/// <summary>An account in a group's or a tab's <b>Account ▸</b> submenu (DESIGN.md §4, "Accounts").</summary>
/// <param name="Pick">The group's or the tab's command, which takes <paramref name="Id"/>.</param>
public sealed record AccountMenuItem(string? Id, string Name, bool IsChecked, IBrush? Brush, IRelayCommand<string?> Pick)
{
    public bool HasBrush => Brush is not null;

    /// <summary>Default, then every other account, with <paramref name="chosen"/> ticked.</summary>
    public static IReadOnlyList<AccountMenuItem> All(AccountsService accounts, string? chosen, IRelayCommand<string?> pick) =>
        [.. AccountChoice.All(accounts, chosen).Select(c => new AccountMenuItem(c.Id, c.Name, c.IsChecked, c.Brush, pick))];
}

/// <summary>The small colored tag that names an account, on a group's label and a tab's row (DESIGN.md §4, "Accounts").</summary>
/// <param name="IsDefault">The Default account's tag, which is muted rather than colored.</param>
public sealed record AccountTag(string Name, IBrush? Brush, IBrush? SoftBrush, bool IsDefault)
{
    /// <summary>The tag for <paramref name="account"/>, or Default's when <paramref name="showDefault"/>; otherwise none.</summary>
    public static AccountTag? For(ClaudeAccount? account, bool showDefault)
    {
        if (account is null)
        {
            return showDefault ? new AccountTag(AccountsService.DefaultName, null, null, IsDefault: true) : null;
        }
        var color = AccountsService.ColorOf(account);
        return new AccountTag(account.Name, new SolidColorBrush(color), new SolidColorBrush(color, 0.18), IsDefault: false);
    }

    public string Tip => IsDefault ? "Runs under the Default account: Claude Code's own sign-in" : $"Runs under {Name}, an API key";
}
