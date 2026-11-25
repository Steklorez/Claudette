using System.Collections.ObjectModel;
using Avalonia.Media;
using Deventry.App.Services;
using Deventry.Core.Accounts;
using Deventry.Core.Credentials;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// One account in Settings → Claude Code → Accounts: its name, tag color, key and spend warnings, and a cloud account's settings
/// (DESIGN.md §4, "Accounts").
/// </summary>
public sealed partial class AccountEditor : ObservableObject
{
    private readonly AccountsService _accounts;
    private readonly Func<string, Task<string?>> _pickFile;
    private readonly Func<string, Task> _openUrl;
    private readonly LocalServersService? _servers;

    public AccountEditor(AccountsService accounts, ClaudeAccount account, Func<string, Task<string?>>? pickFile = null, Func<string, Task>? openUrl = null,
        LocalServersService? servers = null)
    {
        _accounts = accounts;
        _pickFile = pickFile ?? (_ => Task.FromResult<string?>(null));
        _openUrl = openUrl ?? (_ => Task.CompletedTask);
        _servers = servers;
        Account = account;
        Removal = new InlineConfirmation(RemoveAsync);
    }

    public ClaudeAccount Account { get; }

    /// <summary>What it is, beside its name: "API key", "Amazon Bedrock · AWS profile or SSO", "Ollama · qwen3:8b".</summary>
    public string KindText => Account is { Provider: { } provider, Cloud: { } cloud }
        ? $"{provider.Name} · {CloudSignIns.Label(cloud.SignIn, provider)}"
        : Account is { Server: { } server, Local: { } local }
            ? $"{server.Name} · {local.Model}"
            : Account.KindName;

    /// <summary>It spends: an Ollama or llama.cpp account's model runs on the user's own server, which charges nothing.</summary>
    public bool HasSpend => !Account.IsLocal;

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

    /// <summary>
    /// It has a key to change on its own: an API key, or a cloud account that signs in with one. An Ollama or llama.cpp
    /// account's key is in its settings.
    /// </summary>
    public bool HasKey => Account.HasSecret && !Account.IsLocal;

    /// <summary>Its key goes in the environment of its tabs' <c>claude</c>: changing it says so again.</summary>
    public bool ExposesKey => Account.Cloud is { } cloud && CloudSignIns.PutsKeyInEnvironment(cloud.SignIn);

    public string KeyPlaceholder => Account.Provider?.KeyName ?? "sk-ant-…";

    /// <summary>What changing a key that goes in the environment means, as adding it said.</summary>
    public string? KeyExposureNote => ExposesKey
        ? $"Claude Code reads the {Account.Provider?.KeyName} only from its environment, so the commands Claude runs in {Account.Name}'s tabs can read it too. Tabs use a new key when they start again."
        : null;

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
        if (NewKey.Trim() is not { Length: > 0 } key)
        {
            Status = "Paste the new key first.";
            return;
        }
        try
        {
            await _accounts.SetKeyAsync(Account, key);
            Status = Account.IsApiKey
                ? $"Saved the new key in {_accounts.Credentials.Name}. Tabs pick it up within a few minutes, or when they start again."
                : $"Saved the new key in {_accounts.Credentials.Name}. Tabs use it when they start again.";
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

    // ---- A cloud account's settings --------------------------------------------------------------------------------------

    public bool IsCloud => Account.IsCloudProvider;

    /// <summary>Its settings can be changed: a cloud account's, or an Ollama or llama.cpp account's.</summary>
    public bool HasDetails => Account.IsCloudProvider || Account.IsLocal;

    /// <summary>The settings being changed, while <see cref="IsEditingDetails"/>.</summary>
    [ObservableProperty]
    public partial CloudAccountForm? Details { get; private set; }

    [ObservableProperty]
    public partial bool IsEditingDetails { get; private set; }

    /// <summary>An Ollama or llama.cpp account's settings being changed, while <see cref="IsEditingDetails"/>.</summary>
    [ObservableProperty]
    public partial LocalAccountForm? LocalDetails { get; private set; }

    [RelayCommand]
    private void EditDetails()
    {
        if (Account is { Provider: { } provider, Cloud: { } cloud })
        {
            Status = null;
            Details = new CloudAccountForm(provider, _accounts.Credentials.Name, _pickFile, _openUrl, cloud);
            IsEditingDetails = true;
        }
        else if (Account is { Server: { } server, Local: { } local } && _servers is not null)
        {
            Status = null;
            LocalDetails = new LocalAccountForm(server, _servers, _accounts.Credentials.Name, _pickFile, _openUrl, local, () => _accounts.ReadKeyAsync(Account));
            IsEditingDetails = true;
        }
    }

    [RelayCommand]
    private void CancelDetails()
    {
        IsEditingDetails = false;
        Details = null;
        LocalDetails?.ClearSecrets();
        LocalDetails = null;
    }

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        if (LocalDetails is { } local)
        {
            await SaveLocalAsync(local);
            return;
        }
        if (Details is not { } form)
        {
            return;
        }
        if (form.Problem is { } problem)
        {
            Status = problem;
            return;
        }
        _accounts.SetCloud(Account, form.ToSettings());
        Status = $"Saved. Tabs on {Account.Name} use the change when they start again.";
        IsEditingDetails = false;
        Details = null;
        OnPropertyChanged(nameof(KindText));
    }

    private async Task SaveLocalAsync(LocalAccountForm form)
    {
        if (form.Problem is { } problem)
        {
            Status = problem;
            return;
        }
        try
        {
            await _accounts.SetLocalAsync(Account, form.ToSettings(), form.Secret);
            Status = $"Saved. Tabs on {Account.Name} use the change when they start again.";
            IsEditingDetails = false;
            LocalDetails = null;
            OnPropertyChanged(nameof(KindText));
        }
        catch (Exception ex) when (ex is CredentialStoreException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            form.ClearSecrets();
        }
    }

    // ---- Removing it --------------------------------------------------------------------------------------------------

    /// <summary>How many open tabs run under the account: it can't be removed while any do.</summary>
    public int TabsUsing => _accounts.TabsUsing(Account.Id);

    public bool CanRemove => TabsUsing == 0;

    public string RemoveTip => CanRemove
        ? $"Removes {Account.Name}{(Account.HasSecret ? " and its key" : "")}. Folders that used it use Default again."
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

/// <summary>One rule in Settings → Claude Code → What an organization has turned off (DESIGN.md §18, "Remote Control").</summary>
public sealed class OrganizationRuleRow(RemoteControlService service, OrganizationRule rule)
{
    public OrganizationRule Rule { get; } = rule;

    public string Feature => OrganizationRule.FeatureName(Rule.Feature);

    public string Account => Rule.Organization.Length > 0 ? $"{Rule.Email} ({Rule.Organization})" : Rule.Email;

    public string Reason => Rule.Reason;

    public string Since => $"Since {Rule.Since}";

    public IRelayCommand RemoveCommand { get; } = new RelayCommand(() => service.RemoveRule(rule));
}

/// <summary>
/// What Settings → Claude Code → Add an account can add: an Anthropic API key, a cloud provider, or an Ollama or
/// llama.cpp server.
/// </summary>
public sealed record AccountKindChoice(string Label, CloudProvider? Provider, LocalServerKind? Server = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// Settings → Agents → Claude Code → Accounts (DESIGN.md §4, "Accounts"): Default, the accounts tabs can run under besides
/// it, adding an API key or a cloud provider, and which account a folder opened for the first time uses. Each machine sets
/// up its own: nothing here syncs, and keys go only to the OS credential store, and for some providers to their tabs'
/// environment. Then what an organization has turned off for an account (DESIGN.md §18, "Remote Control").
/// </summary>
public sealed partial class ClaudeCodePage
{
    private RemoteControlService RemoteControl => Services.RemoteControl;

    /// <summary>What organizations turned off for the accounts signed in here, each with <b>Remove</b>.</summary>
    public ObservableCollection<OrganizationRuleRow> OrganizationRules { get; }

    public bool HasOrganizationRules => OrganizationRules.Count > 0;

    public string NoOrganizationRulesText => "Nothing. If an organization turns a feature off for your account, it's listed here.";

    private void OnRulesChanged()
    {
        OrganizationRules.Clear();
        foreach (var rule in RemoteControl.Rules)
        {
            OrganizationRules.Add(new OrganizationRuleRow(RemoteControl, rule));
        }
        OnPropertyChanged(nameof(HasOrganizationRules));
    }

    private AccountsService Accounts => Services.Accounts;

    /// <summary>Whether to ask before a session carries on under another account than it started under (DESIGN.md §4).</summary>
    public bool WarnBeforeOtherAccount
    {
        get => Settings.Accounts.WarnBeforeOtherAccount;
        set => Set(value, v => Settings.Accounts.WarnBeforeOtherAccount = v);
    }

    public ObservableCollection<AccountEditor> Editors { get; }

    public bool HasAccounts => Editors.Count > 0;

    public string CredentialStoreName => Accounts.Credentials.Name;

    public bool IsCredentialStoreAvailable => Accounts.Credentials.IsAvailable;

    public string? CredentialStoreUnavailableText => Accounts.Credentials.IsAvailable ? null : Accounts.Credentials.UnavailableReason;

    public string KeyNote =>
        $"Keys are kept in {Accounts.Credentials.Name}, never in Deventry's files, and never synced. A cloud provider's account can sign in without one, through its own tools.";

    /// <summary>What an API key's form says about where its key goes.</summary>
    public string ApiKeyNote =>
        $"Kept in {Accounts.Credentials.Name}. Claude Code asks Deventry for it when a tab starts, so it's never in a command line or the environment.";

    private AccountEditor NewEditor(ClaudeAccount account) => new(Accounts, account, PickFileAsync, Services.Platform.OpenUrlAsync, Services.LocalServers);

    private Task<string?> PickFileAsync(string title) => Services.Platform.PickFileAsync(title);

    // ---- Adding an account ----------------------------------------------------------------------------------------------

    public IReadOnlyList<AccountKindChoice> KindChoices { get; }

    /// <summary>What the new account is: an API key, or a cloud provider, whose form <see cref="NewCloud"/> is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewApiKey), nameof(CanAdd))]
    public partial AccountKindChoice NewKind { get; set; }

    partial void OnNewKindChanged(AccountKindChoice value)
    {
        NewCloud = value.Provider is { } provider ? new CloudAccountForm(provider, Accounts.Credentials.Name, PickFileAsync, Services.Platform.OpenUrlAsync) : null;
        NewLocal = value.Server is { } server ? NewLocalForm(server) : null;
        NewKey = "";
        AddStatus = null;
    }

    public bool IsNewApiKey => NewKind is { Provider: null, Server: null };

    /// <summary>The new Ollama or llama.cpp account's form; null while adding any other kind.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial LocalAccountForm? NewLocal { get; private set; }

    private LocalAccountForm NewLocalForm(LocalServerKind server) =>
        new(server, Services.LocalServers, Accounts.Credentials.Name, PickFileAsync, Services.Platform.OpenUrlAsync);

    /// <summary>The new cloud account's form; null while adding an API key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial CloudAccountForm? NewCloud { get; private set; }

    partial void OnNewCloudChanged(CloudAccountForm? oldValue, CloudAccountForm? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnNewCloudPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnNewCloudPropertyChanged;
        }
    }

    private void OnNewCloudPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudAccountForm.SignIn))
        {
            OnPropertyChanged(nameof(CanAdd));
        }
    }

    /// <summary>
    /// The new account can be added here: one with a key needs the credential store, which a Linux without
    /// <c>secret-tool</c> doesn't have; a cloud account that signs in without one doesn't.
    /// </summary>
    public bool CanAdd => Accounts.Credentials.IsAvailable || NewCloud is { KeepsSecret: false } || NewLocal is not null;

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
        if (NewCloud is { } cloud)
        {
            await AddCloudAsync(name, cloud);
            return;
        }
        if (NewLocal is { } local)
        {
            await AddLocalAsync(name, local);
            return;
        }
        if (name.Length == 0 || NewKey.Trim().Length == 0)
        {
            AddStatus = "Give the account a name, and paste its API key.";
            return;
        }
        try
        {
            await Accounts.AddApiKeyAccountAsync(name, NewKey);
            Added(name);
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

    private async Task AddCloudAsync(string name, CloudAccountForm cloud)
    {
        if (name.Length == 0)
        {
            AddStatus = "Give the account a name.";
            return;
        }
        if ((cloud.Problem ?? cloud.SecretProblem) is { } problem)
        {
            AddStatus = problem;
            return;
        }
        if (cloud.KeepsSecret && !Accounts.Credentials.IsAvailable)
        {
            AddStatus = Accounts.Credentials.UnavailableReason;
            return;
        }
        try
        {
            await Accounts.AddCloudAccountAsync(name, cloud.Provider, cloud.ToSettings(), cloud.Secret);
            Added(name);
            NewCloud = new CloudAccountForm(cloud.Provider, Accounts.Credentials.Name, PickFileAsync, Services.Platform.OpenUrlAsync);
        }
        catch (CredentialStoreException ex)
        {
            AddStatus = ex.Message;
        }
        finally
        {
            cloud.ClearSecrets();
        }
    }

    private async Task AddLocalAsync(string name, LocalAccountForm local)
    {
        if (name.Length == 0)
        {
            AddStatus = "Give the account a name.";
            return;
        }
        if (local.Problem is { } problem)
        {
            AddStatus = problem;
            return;
        }
        if (local.TakesKey && !Accounts.Credentials.IsAvailable)
        {
            AddStatus = Accounts.Credentials.UnavailableReason;
            return;
        }
        try
        {
            await Accounts.AddLocalAccountAsync(name, local.Kind, local.ToSettings(), local.Secret);
            Added(name);
            NewLocal = NewLocalForm(local.Kind);
        }
        catch (Exception ex) when (ex is CredentialStoreException or ArgumentException)
        {
            AddStatus = ex.Message;
        }
        finally
        {
            local.ClearSecrets();
        }
    }

    private void Added(string name)
    {
        AddStatus = $"Added {name}. Choose it from a folder group's Account menu for that folder's new tabs, or from a tab's to move the tab.";
        NewName = "";
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

    private void OnAccountsChanged()
    {
        var all = Accounts.All;
        foreach (var gone in Editors.Where(e => !all.Contains(e.Account)).ToList())
        {
            Editors.Remove(gone);
        }
        foreach (var added in all.Where(a => Editors.All(e => e.Account != a)))
        {
            Editors.Add(NewEditor(added));
        }
        foreach (var editor in Editors)
        {
            editor.Refresh();
        }
        NewFolderChoices = AccountChoice.All(Accounts, Accounts.NewFolderAccount);
        OnPropertyChanged(nameof(NewFolderAccount));
        OnPropertyChanged(nameof(HasAccounts));
        RefreshItems();
    }
}
