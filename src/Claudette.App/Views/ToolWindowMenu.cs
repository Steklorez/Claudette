using Avalonia.Controls;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>
/// A tool window's menu (DESIGN.md §3, "Tool windows"), as Rider's: its view mode, the group to move it to, and Hide. Its
/// icon's right-click and the <b>⋮</b> on its header open the same one, made afresh each time, so it says what holds now.
/// </summary>
public static class ToolWindowMenu
{
    /// <summary>Opens <paramref name="window"/>'s menu at <paramref name="target"/>: under it from a button, or at the pointer.</summary>
    public static void Open(ToolWindowViewModel window, Control target, bool underTarget)
    {
        var menu = new ContextMenu { ItemsSource = Items(window) };
        if (underTarget)
        {
            menu.Placement = PlacementMode.BottomEdgeAlignedRight;
            menu.PlacementTarget = target;
        }
        menu.Open(target);
    }

    private static List<Control> Items(ToolWindowViewModel window) =>
    [
        new MenuItem
        {
            Header = "View mode",
            ItemsSource = new List<Control>
            {
                Choice("Dock Pinned", window.IsPinned, window.PinCommand, "Stays open until you hide it"),
                Choice("Dock Unpinned", window.IsUnpinned, window.UnpinCommand, "Hides when you click outside it"),
            },
        },
        new MenuItem
        {
            Header = "Move to",
            ItemsSource = new List<Control>
            {
                Choice("Right top", window.IsInTop, window.MoveToTopCommand),
                Choice("Right bottom", window.IsInSplit, window.MoveToSplitCommand),
                Choice("Bottom", window.IsInBottom, window.MoveToBottomCommand),
            },
        },
        new Separator(),
        new MenuItem { Header = "Hide", Command = window.HideCommand, IsEnabled = window.IsShowing },
    ];

    private static MenuItem Choice(string header, bool isChecked, System.Windows.Input.ICommand command, string? tip = null)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = isChecked, Command = command };
        if (tip is not null)
        {
            ToolTip.SetTip(item, tip);
        }
        return item;
    }
}
