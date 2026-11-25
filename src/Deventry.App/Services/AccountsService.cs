using System.Globalization;
using Avalonia.Media;
using Deventry.App.ViewModels;
using Deventry.Core;
using Deventry.Core.Accounts;
using Deventry.Core.Credentials;
using Deventry.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>
/// The accounts tabs run Claude Code under besides the Default one (DESIGN.md §4, "Accounts"): API keys and cloud
/// providers. Adding, renaming and removing them, their keys in the OS credential store, what a tab's <c>claude</c>
/// starts with for one, and the warnings when an account's estimated spend passes what the user set.
/// </summary>
public sealed class AccountsService(AppServices services, ICredentialStore credentials)
{
    /// <summary>What the Default account is called: Claude Code's own sign-in, as it is in a terminal.</summary>
    public const string DefaultName = "Default";

    private readonly ILogger _logger = services.Loggers.CreateLogger("Accounts");

    /// <summary>Where accounts' keys are kept. Tests replace it.</summary>
    public ICredentialStore Credentials { get; internal set; } = credentials;

    /// <summary>
    /// Deventry's own executable, which Claude Code runs as an API-key account's <c>apiKeyHelper</c>
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

    /// <summary>
    /// This machine's account named <paramref name="name"/>, without case or spaces at its ends: the same account as
    /// another machine's of that name (DESIGN.md §6, "Sharing across machines"). Null when there's none.
    /// </summary>
    public ClaudeAccount? FindByName(string? name) =>
        name?.Trim() is { Length: > 0 } wanted ? All.FirstOrDefault(a => string.Equals(a.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) : null;

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

    /// <summary>Settings → Claude Code → Accounts → <b>New folders use</b>.</summary>
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
        await Credentials.WriteAsync(AccountSecrets.CredentialKey(account.Id), AccountSecrets.CredentialLabel(account), key.Trim()).ConfigureAwait(false);
        Add(account);
        return account;
    }

    /// <summary>
    /// Adds a cloud provider account. Its key, if its way of signing in has one, goes to the credential store first, so an
    /// account is only added once its key is saved.
    /// </summary>
    /// <exception cref="CredentialStoreException">The key couldn't be saved.</exception>
    /// <exception cref="ArgumentException">What the account says about the provider isn't right (<see cref="CloudProviders.Problem"/>).</exception>
    public async Task<ClaudeAccount> AddCloudAccountAsync(string name, CloudProvider provider, CloudAccountSettings cloud, string? secret)
    {
        if (CloudProviders.Problem(provider, cloud) is { } problem)
        {
            throw new ArgumentException(problem, nameof(cloud));
        }
        var account = new ClaudeAccount
        {
            Name = name.Trim(),
            Kind = provider.Kind,
            Cloud = cloud,
            Color = TabGroupViewModel.ToHex(NextColor()),
        };
        if (CloudSignIns.KeepsSecret(cloud.SignIn))
        {
            if (secret?.Trim() is not { Length: > 0 } trimmed)
            {
                throw new ArgumentException("The account needs its key.", nameof(secret));
            }
            await Credentials.WriteAsync(AccountSecrets.CredentialKey(account.Id), AccountSecrets.CredentialLabel(account), trimmed).ConfigureAwait(false);
        }
        Add(account);
        return account;
    }

    /// <summary>
    /// Adds an Ollama or llama.cpp account (DESIGN.md §18, "Ollama and llama.cpp accounts"). Its key, if its server takes
    /// one, goes to the credential store first, so an account is only added once its key is saved.
    /// </summary>
    /// <exception cref="CredentialStoreException">The key couldn't be saved.</exception>
    /// <exception cref="ArgumentException">What the account says isn't right (<see cref="LocalServers.Problem"/>), or it has no key it says it has.</exception>
    public async Task<ClaudeAccount> AddLocalAccountAsync(string name, LocalServerKind server, LocalServerSettings local, string? key)
    {
        var settings = Normalized(server, local);
        if (settings.HasKey && key?.Trim() is not { Length: > 0 })
        {
            throw new ArgumentException("Paste the server's API key, or untick that it takes one.", nameof(key));
        }
        var account = new ClaudeAccount
        {
            Name = name.Trim(),
            Kind = server.Kind,
            Local = settings,
            Color = TabGroupViewModel.ToHex(NextColor()),
        };
        if (settings.HasKey)
        {
            await Credentials.WriteAsync(AccountSecrets.CredentialKey(account.Id), AccountSecrets.CredentialLabel(account), key!.Trim()).ConfigureAwait(false);
        }
        Add(account);
        return account;
    }

    /// <summary>
    /// Saves what an Ollama or llama.cpp account says, which its tabs use when they start again: a new key replaces its
    /// key, and one it no longer takes is removed from the credential store.
    /// </summary>
    /// <param name="key">A new key; null keeps the one it has.</param>
    /// <exception cref="CredentialStoreException">The key couldn't be saved or removed.</exception>
    /// <exception cref="ArgumentException">It isn't right, or it takes a key it hasn't been given.</exception>
    public async Task SetLocalAsync(ClaudeAccount account, LocalServerSettings local, string? key)
    {
        if (account is not { Server: { } server, Local: { } before })
        {
            return;
        }
        var settings = Normalized(server, local);
        var newKey = key?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
        if (settings.HasKey && newKey is null && !before.HasKey)
        {
            throw new ArgumentException("Paste the server's API key, or untick that it takes one.", nameof(key));
        }
        if (settings.HasKey && newKey is not null)
        {
            await Credentials.WriteAsync(AccountSecrets.CredentialKey(account.Id), AccountSecrets.CredentialLabel(account), newKey).ConfigureAwait(false);
        }
        else if (!settings.HasKey && before.HasKey)
        {
            await Credentials.DeleteAsync(AccountSecrets.CredentialKey(account.Id)).ConfigureAwait(false);
        }
        services.Dispatcher.Post(() =>
        {
            account.Local = settings;
            services.SaveState();
            Changed?.Invoke();
        });
    }

    private static LocalServerSettings Normalized(LocalServerKind server, LocalServerSettings local)
    {
        if (LocalServers.Problem(server, local) is { } problem)
        {
            throw new ArgumentException(problem, nameof(local));
        }
        var settings = local.Copy();
        settings.Address = LocalServers.NormalizeAddress(local.Address)!;
        settings.Model = local.Model.Trim();
        settings.BackgroundModel = local.BackgroundModel?.Trim() is { Length: > 0 } background && background != settings.Model ? background : null;
        settings.ServerPath = local.ServerPath?.Trim() is { Length: > 0 } path ? path : null;
        settings.ServerModel = local.ServerModel?.Trim() is { Length: > 0 } model ? model : null;
        return settings;
    }

    /// <summary>An account's key from the credential store, for Deventry's own requests to its server.</summary>
    /// <exception cref="Core.LocalModels.LocalServerException">It couldn't be read.</exception>
    public async Task<string?> ReadKeyAsync(ClaudeAccount account)
    {
        try
        {
            return await Credentials.ReadAsync(AccountSecrets.CredentialKey(account.Id)).ConfigureAwait(false);
        }
        catch (CredentialStoreException ex)
        {
            throw new Core.LocalModels.LocalServerException($"Deventry couldn't read {account.Name}'s key from {Credentials.Name}: {ex.Message}", ex);
        }
    }

    private void Add(ClaudeAccount account) => services.Dispatcher.Post(() =>
    {
        services.State.Accounts.Add(account);
        services.SaveState();
        Changed?.Invoke();
    });

    /// <summary>Replaces an account's key in the credential store: an API key's, or a cloud provider's.</summary>
    /// <exception cref="CredentialStoreException">The key couldn't be saved.</exception>
    public Task SetKeyAsync(ClaudeAccount account, string key) =>
        Credentials.WriteAsync(AccountSecrets.CredentialKey(account.Id), AccountSecrets.CredentialLabel(account), key.Trim());

    /// <summary>Saves what a cloud account says about its provider, which its tabs use when they start again.</summary>
    /// <exception cref="ArgumentException">It isn't right (<see cref="CloudProviders.Problem"/>).</exception>
    public void SetCloud(ClaudeAccount account, CloudAccountSettings cloud)
    {
        if (account.Provider is not { } provider)
        {
            return;
        }
        if (CloudProviders.Problem(provider, cloud) is { } problem)
        {
            throw new ArgumentException(problem, nameof(cloud));
        }
        account.Cloud = cloud;
        services.SaveState();
        Changed?.Invoke();
    }

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
        // An account without a secret needs no credential store, which a Linux without secret-tool doesn't have.
        if (account.HasSecret || Credentials.IsAvailable)
        {
            await Credentials.DeleteAsync(AccountSecrets.CredentialKey(account.Id)).ConfigureAwait(false);
        }
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
    /// What a tab's <c>claude</c> starts with for <paramref name="account"/> (<see cref="AccountLaunch"/>): null for
    /// Default, which starts as before. A cloud account whose key goes in the environment reads it from the credential
    /// store here.
    /// </summary>
    /// <exception cref="InvalidOperationException">Deventry's path can't go in the helper's command, or the key can't be read.</exception>
    public async Task<AccountLaunch?> LaunchForAsync(ClaudeAccount? account)
    {
        if (account is null)
        {
            return null;
        }
        // Only an account whose key the helper gives needs Deventry's path in a command; an Ollama without a key doesn't.
        var usesHelper = account.IsApiKey || account.Local is { HasKey: true };
        var executable = HelperExecutable ?? "";
        if (usesHelper && !ApiKeyHelper.IsSafePath(executable))
        {
            throw new InvalidOperationException($"Claude Code can't be given {account.Name}'s credentials: Deventry's own path ({HelperExecutable ?? "unknown"}) can't go in a command as it is.");
        }
        string? key = null;
        if (account.Cloud is { } cloud && CloudSignIns.PutsKeyInEnvironment(cloud.SignIn))
        {
            try
            {
                key = await Credentials.ReadAsync(AccountSecrets.CredentialKey(account.Id)).ConfigureAwait(false);
            }
            catch (CredentialStoreException ex)
            {
                throw new InvalidOperationException($"Deventry couldn't read {account.Name}'s key from {Credentials.Name}: {ex.Message}", ex);
            }
        }
        return AccountLaunch.For(account, executable, key);
    }

    // ---- Spend warnings ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Other machines' turns under API-key accounts of these names were added to the usage history (DESIGN.md §6, "Sharing
    /// across machines"): this machine's accounts of the same names may have passed their warnings.
    /// </summary>
    public async Task OnTurnsImportedAsync(IReadOnlyCollection<string> accountNames)
    {
        foreach (var account in accountNames.Select(FindByName).OfType<ClaudeAccount>().DistinctBy(a => a.Id).ToArray())
        {
            await OnSpendRecordedAsync(account.Id);
        }
    }

    /// <summary>
    /// A turn of <paramref name="accountId"/>'s was recorded, or one running under it spent more: warns once a day and once
    /// a month when its estimated spend passes what Settings → Claude Code → Accounts says. The spend counts other machines' turns under
    /// an account of the same name (DESIGN.md §6, "Sharing across machines"), and the turns running here as they go. Reads
    /// the usage history off the UI thread.
    /// </summary>
    public async Task OnSpendRecordedAsync(string accountId)
    {
        if (Find(accountId) is not { } account || account is { DailySpendAlert: null, MonthlySpendAlert: null } || services.Usage is not { } usage)
        {
            return;
        }
        // Both sent already: nothing to read.
        var sentFor = services.Time.GetLocalNow();
        if ((account.DailySpendAlert is null || account.DailyAlertSentFor == sentFor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            && (account.MonthlySpendAlert is null || account.MonthlyAlertSentFor == sentFor.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
        {
            return;
        }
        var now = services.Time.GetLocalNow();
        var (today, month) = (SpendPeriods.StartOfDay(now), SpendPeriods.StartOfMonth(now));
        var name = account.Name;
        double spentToday, spentThisMonth;
        try
        {
            (spentToday, spentThisMonth) = await Task.Run(() => (usage.GetSpend(account.Id, today, name), usage.GetSpend(account.Id, month, name))).ConfigureAwait(false);
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
                    $"The estimated spend on {SpendOn(account)} passed {Formats.Dollars((double)daily)} today.");
            }
            if (account.MonthlySpendAlert is { } monthly && spentThisMonth >= (double)monthly && account.MonthlyAlertSentFor != monthKey)
            {
                account.MonthlyAlertSentFor = monthKey;
                services.SaveState();
                services.Notifications.Notify(NotificationKind.UsageAlert, $"{account.Name}: {Formats.Dollars(spentThisMonth)} this month",
                    $"The estimated spend on {SpendOn(account)} passed {Formats.Dollars((double)monthly)} this month.");
            }
        });
    }

    /// <summary>How often a turn running checks its account's spend warnings as it goes.</summary>
    internal static readonly TimeSpan RunningCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>When each account's warnings were last checked for a turn running, by its id.</summary>
    private readonly Dictionary<string, DateTimeOffset> _runningChecked = new(StringComparer.Ordinal);

    /// <summary>
    /// A turn running under <paramref name="accountId"/> spent more (on the UI thread): its warnings are checked, at most
    /// once a minute, so a long turn passes one as it does rather than when it ends.
    /// </summary>
    public void OnRunningSpendChanged(string accountId)
    {
        var now = services.Time.GetUtcNow();
        if (_runningChecked.TryGetValue(accountId, out var last) && now - last < RunningCheckInterval)
        {
            return;
        }
        _runningChecked[accountId] = now;
        _ = OnSpendRecordedAsync(accountId);
    }

    /// <summary>What a spend warning says the spend is on: "Work's API key", "Studio's Amazon Bedrock account". Local accounts have none.</summary>
    public static string SpendOn(ClaudeAccount account) =>
        account.Provider is { } provider ? $"{account.Name}'s {provider.Name} account" : $"{account.Name}'s API key";
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
