namespace Deventry.App.ViewModels;

/// <summary>Opening a tab's token breakdown in its window, which the main window keeps (DESIGN.md §18, "Opening it").</summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// A tab's token breakdown was asked for, on a turn by its id or its number, or as it is: the main window opens its
    /// window, or brings it to the front.
    /// </summary>
    public event Action<TabViewModel, string?, int?>? TokenBreakdownRequested;

    internal void ShowTokenBreakdown(TabViewModel tab, string? turnId, int? number) => TokenBreakdownRequested?.Invoke(tab, turnId, number);
}
