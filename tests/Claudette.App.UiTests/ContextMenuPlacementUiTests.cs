using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>
/// Right-click menus open at the pointer, anchored there, so their window grows with them (DESIGN.md §3). Headless, a menu
/// is drawn in the window rather than in a window of its own, so these check the placement; the cut-off menu itself was
/// only on the desktop.
/// </summary>
public class ContextMenuPlacementUiTests
{
    [AvaloniaFact]
    public async Task A_right_click_opens_a_tabs_menu_anchored_where_it_was_clicked_and_the_keyboard_under_the_row()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        // A tick and a submenu arrow beside its longest entry: the menu that was cut off.
        tab.SetSyncToLibrary(true);
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabrow"));
        var menu = row.ContextMenu!;

        var at = row.TranslatePoint(new Point(40, 10), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        UiText.Settle(window);

        Assert.True(menu.IsOpen);
        Assert.Equal(PlacementMode.AnchorAndGravity, menu.Placement);
        Assert.Equal(PopupAnchor.TopLeft, menu.PlacementAnchor);
        Assert.Equal(PopupGravity.BottomRight, menu.PlacementGravity);
        Assert.Equal(at, menu.TranslatePoint(default, window));
        // Every entry gets the width it needs.
        Assert.All(menu.Items.OfType<MenuItem>().Where(i => i.IsVisible), item => Assert.True(item.Bounds.Width >= item.DesiredSize.Width, $"{item.Header} is cut off"));
        menu.Close();
        UiText.Settle(window);

        // The menu key: under the row, wherever the last click was.
        row.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
        UiText.Settle(window);

        Assert.True(menu.IsOpen);
        Assert.Null(menu.PlacementRect);
        Assert.Equal(row.TranslatePoint(new Point(0, row.Bounds.Height), window)!.Value.Y, menu.TranslatePoint(default, window)!.Value.Y, 1);
        menu.Close();
        window.Close();
    }
}
