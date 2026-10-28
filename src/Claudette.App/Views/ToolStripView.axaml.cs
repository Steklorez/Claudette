using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>
/// An auto-hidden edge's tabs (DESIGN.md §3, "Tool windows"): the dock decides when a window slides out, on the injected
/// clock, from where the pointer is and what's clicked.
/// </summary>
public partial class ToolStripView : UserControl
{
    public ToolStripView() => InitializeComponent();

    private static ToolWindowViewModel? WindowOf(object? sender) => (sender as Control)?.DataContext as ToolWindowViewModel;

    private ToolDockViewModel? Dock => (DataContext as ToolEdgeViewModel)?.Dock;

    private void OnTabEntered(object? sender, PointerEventArgs e)
    {
        if (WindowOf(sender) is { } window)
        {
            Dock?.PointAtTab(window);
        }
    }

    private void OnTabExited(object? sender, PointerEventArgs e)
    {
        if (WindowOf(sender) is { } window)
        {
            Dock?.LeaveTab(window);
        }
    }

    private void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (WindowOf(sender) is { } window)
        {
            Dock?.ClickTab(window);
        }
    }
}
