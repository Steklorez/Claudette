using CommunityToolkit.Mvvm.Input;
using Deventry.Core;
using Deventry.Core.Sessions;

namespace Deventry.App.ViewModels;

/// <summary>The tab's side of its token breakdown (DESIGN.md §18, "Token breakdown").</summary>
public sealed partial class TabViewModel
{
    /// <summary>The git branch the tab works on, for the breakdown window's line.</summary>
    internal string? TokenBranchName => BranchName;

    /// <summary>A model's name as the tab shows it (*Opus 5.5*), or as its id says when the tab doesn't know it.</summary>
    internal string ModelNameFor(string model) => ModelDisplayName(model) is { } name && name != model ? name : Formats.ModelName(model);

    /// <summary><b>Token breakdown…</b>: the tab's window, opened or brought to the front (DESIGN.md §18, "Opening it").</summary>
    [RelayCommand]
    private void ShowTokenBreakdown() => _shell.ShowTokenBreakdown(this, null, null);

    /// <summary>A turn's card's <b>Breakdown ›</b>: the tab's window, on that turn (DESIGN.md §18, "A turn's card").</summary>
    [RelayCommand]
    private void ShowTokenBreakdownAt(string? turnId) => _shell.ShowTokenBreakdown(this, turnId, null);

    /// <summary>The tab's window, on a turn by its id or its number, or as it is with neither.</summary>
    internal void ShowTokenBreakdownOn(string? turnId, int? number) => _shell.ShowTokenBreakdown(this, turnId, number);

    /// <summary>
    /// The tab's tokens as the Tokens window, the composer's popup and the info card show them (DESIGN.md §18, "The Tokens
    /// tool window").
    /// </summary>
    public TabTokensViewModel Tokens => field ??= new TabTokensViewModel(_services, this);

    /// <summary>The model the tab's next request goes to, for what the next message will cost.</summary>
    internal string? TokenModelId => _modelId;

    /// <summary>When the tab's prompt cache expires, and whether it's the hour's; null while there's none to say.</summary>
    internal (DateTimeOffset ExpiresAt, bool Hour)? TokenCacheExpiry =>
        PromptCacheLifetime is { } lifetime && PromptCacheClock.RefreshedAt is { } refreshed
            ? (refreshed + lifetime.Lifetime, lifetime.Lifetime >= PromptCache.OneHour)
            : null;

    /// <summary>The tab's share of the session limit was read: the info card's Tokens row says it.</summary>
    internal void TokensInfoChanged() => OnPropertyChanged(nameof(InfoRows));
}
