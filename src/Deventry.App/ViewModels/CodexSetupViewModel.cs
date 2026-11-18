using Deventry.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// Codex found or installed, and signed in (DESIGN.md §18, "Codex, a second agent", "Installing and signing in"): Settings
/// → Agents → Codex, and the offer of it on the setup and sign-in screens. Dispose it with what shows it.
/// </summary>
public sealed partial class CodexSetupViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;

    public CodexSetupViewModel(AppServices services)
    {
        _services = services;
        services.CodexChanged += OnCodexChanged;
        _ = RefreshSignInAsync();
    }

    public void Dispose() => _services.CodexChanged -= OnCodexChanged;

    private void OnCodexChanged()
    {
        OnPropertyChanged(nameof(FoundText));
        OnPropertyChanged(nameof(IsFound));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(Summary));
        _ = RefreshSignInAsync();
    }

    // ---- Found ---------------------------------------------------------------------------------------------------------

    public bool IsFound => _services.CodexInstall is not null;

    /// <summary>"Codex 0.169.0, at C:\…\codex.exe", or why it can't be used.</summary>
    public string FoundText => _services.CodexInstall is { } install
        ? $"Codex {install.Version}, at {install.Path}"
        : _services.CodexFound?.Detail ?? "Looking for Codex…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    public partial bool IsBusy { get; private set; }

    /// <summary><b>Look again</b>: as at launch, with the path Settings names.</summary>
    [RelayCommand]
    private async Task LookAgainAsync()
    {
        IsBusy = true;
        try
        {
            await _services.LocateCodexAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool CanInstall => !IsFound && !IsBusy;

    /// <summary>The command <b>Install Codex</b> runs, shown before it does.</summary>
    public string InstallCommandText => _services.CreateCodexInstaller().CommandText;

    [ObservableProperty]
    public partial string? InstallOutput { get; private set; }

    /// <summary><b>Install Codex</b>: Codex's own installer, then looking again.</summary>
    [RelayCommand]
    private async Task InstallAsync()
    {
        IsBusy = true;
        InstallOutput = null;
        try
        {
            var installer = _services.CreateCodexInstaller();
            var result = await Task.Run(() => installer.InstallAsync(line => _services.Dispatcher.Post(() => InstallOutput = line)));
            InstallOutput = result.Message;
            if (result.Succeeded)
            {
                await _services.LocateCodexAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Signed in -----------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn), nameof(CanSignOut), nameof(Summary))]
    public partial bool IsSignedIn { get; private set; }

    [ObservableProperty]
    public partial string SignInText { get; private set; } = "Not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn), nameof(CanSignOut))]
    public partial bool IsSigningIn { get; private set; }

    public bool CanSignIn => IsFound && !IsSignedIn && !IsSigningIn;

    public bool CanSignOut => IsFound && IsSignedIn && !IsSigningIn;

    /// <summary>Where Codex stands, for the setup and sign-in screens: what's left to do before Codex tabs can start.</summary>
    public string Summary => !IsFound && _services.CodexFound is null ? "Looking for Codex…"
        : !IsFound ? "Codex isn't installed."
        : IsSignedIn ? "Codex is ready: a folder group's menu opens Codex tabs."
        : "Codex is installed. Sign in to use it.";

    private async Task RefreshSignInAsync()
    {
        if (_services.CodexAuth is not { } auth)
        {
            IsSignedIn = false;
            SignInText = "Codex isn't found yet.";
            return;
        }
        try
        {
            var status = await Task.Run(() => auth.GetStatusAsync());
            IsSignedIn = status.SignedIn;
            SignInText = status.Text;
        }
        catch (Exception ex)
        {
            SignInText = $"Couldn't ask Codex: {ex.Message}";
        }
    }

    /// <summary><b>Sign in</b>: <c>codex login</c>, which opens the browser; done once Codex says it's signed in.</summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        if (_services.CodexAuth is not { } auth)
        {
            return;
        }
        IsSigningIn = true;
        SignInText = "Finish signing in in your browser…";
        try
        {
            var result = await Task.Run(() => auth.LoginAsync());
            IsSignedIn = result.SignedIn;
            SignInText = result.Text;
        }
        catch (Exception ex)
        {
            SignInText = $"Couldn't sign in: {ex.Message}";
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (_services.CodexAuth is not { } auth)
        {
            return;
        }
        try
        {
            await Task.Run(() => auth.LogoutAsync());
        }
        finally
        {
            await RefreshSignInAsync();
        }
    }
}
