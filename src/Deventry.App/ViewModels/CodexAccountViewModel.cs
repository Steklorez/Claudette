using Deventry.App.Services;
using Deventry.Core.Sessions;

namespace Deventry.App.ViewModels;

/// <summary>
/// Codex's account in the header while a Codex tab is selected (DESIGN.md §18, "The header follows the selected tab's
/// agent"): its email and plan, as the last Codex session that started said them (<c>account/read</c>), in place of
/// Claude Code's. Codex signs itself in, so its menu leads to Settings → Agents → Codex rather than signing out here.
/// Dispose it with the window.
/// </summary>
public sealed class CodexAccountViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;

    public CodexAccountViewModel(AppServices services)
    {
        _services = services;
        services.CodexAccountChanged += OnChanged;
    }

    public void Dispose() => _services.CodexAccountChanged -= OnChanged;

    private AccountInfo? Account => _services.CodexAccount;

    /// <summary>A Codex session has said which account Codex is signed in to.</summary>
    public bool IsKnown => Account is not null;

    public string? Email => Account?.Email;

    /// <summary>"Pro plan", from the plan Codex says; null for an API key, or a plan it doesn't say.</summary>
    public string? PlanText => PlanName(Account?.SubscriptionType);

    /// <summary>Signed in with an OpenAI API key rather than a ChatGPT account.</summary>
    public bool IsApiKey => string.Equals(Account?.TokenSource, "apiKey", StringComparison.OrdinalIgnoreCase);

    /// <summary>The header's name: the email, or what Codex signs in with, or just Codex until a session has said.</summary>
    public string HeaderName => Email ?? (IsApiKey ? "API key" : IsKnown ? "ChatGPT" : "Codex");

    /// <summary>The menu's first line.</summary>
    public string SignedInText => !IsKnown
        ? "Codex says which account it's signed in to once a Codex tab has started."
        : IsApiKey ? "Codex is signed in with an OpenAI API key." : "Signed in to Codex as";

    /// <summary>"Pro plan" for <c>pro</c>, as Codex names its plans; null for none.</summary>
    internal static string? PlanName(string? planType) => planType switch
    {
        null or "" or "unknown" => null,
        _ => $"{char.ToUpperInvariant(planType[0])}{planType[1..]} plan",
    };

    private void OnChanged()
    {
        OnPropertyChanged(nameof(IsKnown));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(PlanText));
        OnPropertyChanged(nameof(IsApiKey));
        OnPropertyChanged(nameof(HeaderName));
        OnPropertyChanged(nameof(SignedInText));
    }
}
