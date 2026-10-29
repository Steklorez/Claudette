using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>A group's tool window, with Rider's header over it (DESIGN.md §3, "Tool windows").</summary>
public partial class ToolPanelView : UserControl
{
    /// <summary>The session's group color as a brush, for the dot beside its name; none without one.</summary>
    public static readonly IValueConverter ColorBrush = new FuncValueConverter<Color?, IBrush?>(c => c is { } color ? new SolidColorBrush(color) : null);

    public ToolPanelView() => InitializeComponent();

    /// <summary>The <b>⋮</b>: the window's menu, under it.</summary>
    private void OnOptionsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && DataContext is ToolGroupViewModel { Showing: { } window })
        {
            ToolWindowMenu.Open(window, button, underTarget: true);
        }
    }
}
