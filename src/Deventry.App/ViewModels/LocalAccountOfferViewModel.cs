using Deventry.App.Services;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.Accounts;
using Deventry.Core.Credentials;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The sign-in screen's <b>Use Ollama or llama.cpp instead</b> (DESIGN.md §18, "Without a Claude account"): an Ollama
/// or llama.cpp account added there, with the same form as Settings, made what new folders use, and the tabs opened,
/// with no Claude sign-in.
/// </summary>
public sealed partial class LocalAccountOfferViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<Task> _onAdded;

    /// <param name="onAdded">Goes on from the sign-in screen, as a sign-in would.</param>
    public LocalAccountOfferViewModel(AppServices services, Func<Task> onAdded)
    {
        _services = services;
        _onAdded = onAdded;
        Kinds = [.. LocalServers.All.Select(k => new AccountKindChoice(k.Name, null, k))];
        Kind = Kinds[0];
    }

    public const string OfferText = "Use Ollama or llama.cpp instead";

    public string Note => "Claude Code can run on a model your own Ollama or llama-server serves, with no Claude account. You can sign in to Claude later, from the account menu.";

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Form?.ClearSecrets();
        Status = null;
    }

    public IReadOnlyList<AccountKindChoice> Kinds { get; }

    [ObservableProperty]
    public partial AccountKindChoice Kind { get; set; }

    partial void OnKindChanged(AccountKindChoice value)
    {
        Form?.ClearSecrets();
        Form = new LocalAccountForm(value.Server!, _services.LocalServers, _services.Accounts.Credentials.Name,
            title => _services.Platform.PickFileAsync(title), _services.Platform.OpenUrlAsync);
        Status = null;
    }

    [ObservableProperty]
    public partial LocalAccountForm? Form { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    public partial bool IsAdding { get; private set; }

    /// <summary>Adds the account, makes it what new folders use, and opens the tabs.</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (Form is not { } form)
        {
            return;
        }
        var name = Name.Trim() is { Length: > 0 } named ? named : form.Kind.Name;
        if (form.Problem is { } problem)
        {
            Status = problem;
            return;
        }
        if (form.TakesKey && !_services.Accounts.Credentials.IsAvailable)
        {
            Status = _services.Accounts.Credentials.UnavailableReason;
            return;
        }
        IsAdding = true;
        try
        {
            var account = await _services.Accounts.AddLocalAccountAsync(name, form.Kind, form.ToSettings(), form.Secret);
            // The account is added on the UI thread, in a step posted before this one: once it's there, new folders use
            // it, and the sign-in screen goes on, finding it.
            _services.Dispatcher.Post(() =>
            {
                _services.Accounts.NewFolderAccount = account.Id;
                _ = _onAdded();
            });
        }
        catch (Exception ex) when (ex is CredentialStoreException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            form.ClearSecrets();
            IsAdding = false;
        }
    }
}
