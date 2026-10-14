using System.Globalization;
using Avalonia.Media;
using Claudette.App.ViewModels;
using Claudette.Core;
using Claudette.Core.Accounts;
using Claudette.Core.Credentials;
using Claudette.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Claudette.App.Services;

/// <summary>
/// The accounts tabs run Claude Code under besides the Default one (DESIGN.md §4, "Accounts"): adding, renaming and
/// removing them, their keys in the OS credential store, what a tab's <c>claude</c> starts with for one, and the warnings
/// when an API key's estimated spend passes what the user set.
/// </summary>
public sealed class AccountsService(AppServices services, ICredentialStore credentials)
{
    /// <summary>What the Default account is called: Claude Code's own sign-in, as it is in a terminal.</summary>
    public const string DefaultName = "Default";

    private readonly ILogger _logger = services.Loggers.CreateLogger("Accounts");

    /// <summary>Where API keys are kept. Tests replace it.</summary>
    public ICredentialStore Credentials { get; internal set; } = credentials;

    /// <summary>
    /// Claudette's own executable, which Claude Code runs as an API-key account's <c>apiKeyHelper</c>
    /// (<see cref="ApiKeyHelper"/>). Tests replace it.
    /// </summary>
    public string? HelperExecutable { get; internal set; } = Environment.ProcessPath;

    /// <summary>Raised when an account is added, renamed, recolored or removed, or which one a folder uses changes.</summary>
    public event Action? Changed;

    /// <summary>The open tabs, which the shell gives it: an account an open tab uses can't be removed.</summary>
    public Func<IReadOnlyList<TabState>> OpenTabs { get; set; } = () => [];

    /// <summary>How many open tabs run under the account.</summary>
    public int TabsUsing(string accountId) => OpenTabs().Count(t => t.AccountId == accountId);

    /// <summary>Every account besides Default, in the order they were added.</summary>
    public IReadOnlyList<ClaudeAccount> All => [.. services.State.Accounts.Where(a => AccountState.Find(services.State, a.Id) is not null)];

    public bool HasAccounts => All.Count > 0;

    /// <summary>The account with <paramref name="id"/>; null for Default, or for one that's gone.</summary>
    public ClaudeAccount? Find(string? id) => AccountState.Find(services.State, id);

    public string NameOf(string? id) => Find(id)?.Name ?? DefaultName;

    /// <summary>The color of an account's tag; the muted text color's stand-in for Default, which has no tag.</summary>
    public static Color ColorOf(ClaudeAccount? account) =>
        account is not null && TabGroupViewModel.TryParseHex(account.Color, out var color) ? color : TabGroupViewModel.Palette[0].Color;

    /// <summary>The account a new tab in <paramref name="folder"/> starts on (its group's, else the one new folders use).</summary>
    public string? ForFolder(string folder) => AccountState.ForFolder(services.State, folder);

    /// <summary>Sets the account <paramref name="folder"/>'s new tabs use, and saves it.</summary>
    public void SetForFolder(string folder, string? accountId)
    {
        AccountState.RememberForFolder(services.State, folder, Find(accountId)?.Id);
        services.SaveState();
        Changed?.Invoke();
    }

    /// <summary>Settings → Accounts → <b>New folders use</b>.</summary>
    public string? NewFolderAccount
    {
        get => Find(services.State.NewFolderAccount)?.Id;
        set
        {
            services.State.NewFolderAccount = Find(value)?.Id;
            services.SaveState();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Adds an API-key account: the key goes to the credential store first, so an account is only added once its key is
    /// saved. Its tag takes the first group color no other account has.
    /// </summary>
    /// <exception cref="CredentialStoreException">The key couldn't be saved.</exception>
    public async Task<ClaudeAccount> AddApiKeyAccountAsync(string name, string key)
    {
        var account = new ClaudeAccount
        {
            Name = name.Trim(),
            Kind = AccountKinds.ApiKey,
            Color = TabGroupViewModel.ToHex(NextColor()),
        };
        await Credentials.WriteAsync(ApiKeyHelper.CredentialKey(account.Id), ApiKeyHelper.CredentialLabel(account.Name), key.Trim()).ConfigureAwait(false);
        services.Dispatcher.Post(() =>
        {
            services.State.Accounts.Add(account);
            services.SaveState();
            Changed?.Invoke();
        });
        return account;
    }

    /// <summary>Replaces an API-key account's key in the credential store.</summary>
    /// <exception cref="CredentialStoreException">The key couldn't be saved.</exception>
    public Task SetKeyAsync(ClaudeAccount account, string key) =>
        Credentials.WriteAsync(ApiKeyHelper.CredentialKey(account.Id), ApiKeyHelper.CredentialLabel(account.Name), key.Trim());

    public void Rename(ClaudeAccount account, string name)
    {
        if (name.Trim() is { Length: > 0 } trimmed && trimmed != account.Name)
        {
            account.Name = trimmed;
            services.SaveState();
            Changed?.Invoke();
        }
    }

    public void SetColor(ClaudeAccount account, Color color)
    {
        account.Color = TabGroupViewModel.ToHex(color);
        services.SaveState();
        Changed?.Invoke();
    }

    /// <summary>Saves what the account's spend warnings are set to, and that they're changed.</summary>
    public void SpendAlertsChanged()
    {
        services.SaveState();
        Changed?.Invoke();
    }

    /// <summary>
    /// Removes an account and its key. The folders that used it use Default again. The caller makes sure no open tab
    /// uses it.
    /// </summary>
    /// <exception cref="CredentialStoreException">The key couldn't be removed; the account stays.</exception>
    public async Task RemoveAsync(ClaudeAccount account)
    {
        await Credentials.DeleteAsync(ApiKeyHelper.CredentialKey(account.Id)).ConfigureAwait(false);
        services.Dispatcher.Post(() =>
        {
            AccountState.Forget(services.State, account.Id);
            services.SaveState();
            Changed?.Invoke();
        });
    }

    private Color NextColor() => GroupColors.Next([.. All.Select(ColorOf)]);

    // ---- Starting claude --------------------------------------------------------------------------------------------

    /// <summary>
    /// What a tab's <c>claude</c> starts with for <paramref name="account"/>: nothing more for Default; for an API key,
    /// the <c>--settings</c> naming its key helper, and without the variables Claude Code would take over it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Claudette's path can't go in the helper's command.</exception>
    public (string? Settings, IReadOnlyDictionary<string, string?> Environment) LaunchFor(ClaudeAccount? account)
    {
        if (account is null)
        {
            return (null, new Dictionary<string, string?>());
        }
        if (HelperExecutable is not { } executable || !ApiKeyHelper.IsSafePath(executable))
        {
            throw new InvalidOperationException($"Claude Code can't be given {account.Name}'s key: Claudette's own path ({HelperExecutable ?? "unknown"}) can't go in a command as it is.");
        }
        return (ApiKeyHelper.Settings(executable, account.Id), ApiKeyHelper.OutrankingVariables.ToDictionary(v => v, string? (_) => null));
    }

    // ---- Spend warnings ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A turn of <paramref name="accountId"/>'s was recorded: warns once a day and once a month when its estimated spend
    /// passes what Settings → Accounts says. Reads the usage history off the UI thread.
    /// </summary>
    public async Task OnSpendRecordedAsync(string accountId)
    {
        if (Find(accountId) is not { } account || account is { DailySpendAlert: null, MonthlySpendAlert: null } || services.Usage is not { } usage)
        {
            return;
        }
        var now = services.Time.GetLocalNow();
        var (today, month) = (SpendPeriods.StartOfDay(now), SpendPeriods.StartOfMonth(now));
        double spentToday, spentThisMonth;
        try
        {
            (spentToday, spentThisMonth) = await Task.Run(() => (usage.Store.GetSpend(account.Id, today), usage.Store.GetSpend(account.Id, month))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't read {Account}'s spend for its warnings.", account.Name);
            return;
        }
        services.Dispatcher.Post(() =>
        {
            var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var monthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (account.DailySpendAlert is { } daily && spentToday >= (double)daily && account.DailyAlertSentFor != day)
            {
                account.DailyAlertSentFor = day;
                services.SaveState();
                services.Notifications.Notify(NotificationKind.UsageAlert, $"{account.Name}: {Formats.Dollars(spentToday)} today",
                    $"The estimated spend on {account.Name}'s API key passed {Formats.Dollars((double)daily)} today.");
            }
            if (account.MonthlySpendAlert is { } monthly && spentThisMonth >= (double)monthly && account.MonthlyAlertSentFor != monthKey)
            {
                account.MonthlyAlertSentFor = monthKey;
                services.SaveState();
                services.Notifications.Notify(NotificationKind.UsageAlert, $"{account.Name}: {Formats.Dollars(spentThisMonth)} this month",
                    $"The estimated spend on {account.Name}'s API key passed {Formats.Dollars((double)monthly)} this month.");
            }
        });
    }
}

/// <summary>Where the spend periods start, in local time (DESIGN.md §4, "Accounts").</summary>
public static class SpendPeriods
{
    public static DateTimeOffset StartOfDay(DateTimeOffset local) => new(local.Date, local.Offset);

    /// <summary>The week starts on the first day of the week of the user's culture.</summary>
    public static DateTimeOffset StartOfWeek(DateTimeOffset local)
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var back = ((int)local.DayOfWeek - (int)first + 7) % 7;
        return StartOfDay(local).AddDays(-back);
    }

    public static DateTimeOffset StartOfMonth(DateTimeOffset local) => new(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
}
