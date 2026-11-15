using CommunityToolkit.Mvvm.Input;
using Deventry.Core;

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
    private void ShowTokenBreakdown() => _shell.ShowTokenBreakdown(this);
}
