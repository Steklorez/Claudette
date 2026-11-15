namespace Deventry.App.ViewModels;

/// <summary>Opening a tab's token breakdown in its window, which the main window keeps (DESIGN.md §18, "Opening it").</summary>
public sealed partial class ShellViewModel
{
    /// <summary>A tab's token breakdown was asked for: the main window opens its window, or brings it to the front.</summary>
    public event Action<TabViewModel>? TokenBreakdownRequested;

    internal void ShowTokenBreakdown(TabViewModel tab) => TokenBreakdownRequested?.Invoke(tab);
}
