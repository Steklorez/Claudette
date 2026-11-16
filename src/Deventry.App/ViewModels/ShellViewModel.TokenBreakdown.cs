namespace Deventry.App.ViewModels;

/// <summary>Opening a tab's token breakdown in its window, which the main window keeps (DESIGN.md §18, "Opening it").</summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// A tab's token breakdown was asked for, on a turn by its id or its number, or as it is: the main window opens its
    /// window, or brings it to the front.
    /// </summary>
    public event Action<ITokenBreakdownSubject, string?, int?>? TokenBreakdownRequested;

    internal void ShowTokenBreakdown(TabViewModel tab, string? turnId, int? number) => TokenBreakdownRequested?.Invoke(new OpenTabBreakdown(tab), turnId, number);

    /// <summary>
    /// A tab the Usage panel lists (DESIGN.md §18, "The Usage panel"): the open tab's own breakdown when it's open here,
    /// otherwise what the usage history has of it.
    /// </summary>
    internal void ShowTokenBreakdown(HistoryTabBreakdown tab)
    {
        ITokenBreakdownSubject subject = tab.Machine is null && AllTabs.FirstOrDefault(t => t.Id == tab.Id) is { } open ? new OpenTabBreakdown(open) : tab;
        TokenBreakdownRequested?.Invoke(subject, null, null);
    }
}
