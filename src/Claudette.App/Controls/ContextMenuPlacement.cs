using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;

namespace Claudette.App.Controls;

/// <summary>
/// Opens a right-click menu at the pointer, as Avalonia's <see cref="PlacementMode.Pointer"/> does, but anchored at that
/// point, so the menu's window grows with the menu (DESIGN.md §3). Avalonia 12.1 never resizes a popup placed at the
/// pointer after its first layout (<c>PopupRoot.ArrangeSetBounds</c>), and a menu's first layout is too narrow: its
/// items share columns for ticks, icons and submenu arrows, which a <see cref="Grid"/> only widens after a layout pass.
/// Where a menu is a window of its own, as on the desktop, its longest items were cut off.
/// </summary>
internal static class ContextMenuPlacement
{
    /// <summary>Set on a menu this anchors, which no longer says <see cref="PlacementMode.Pointer"/>.</summary>
    private static readonly AttachedProperty<bool> AnchoredProperty =
        AvaloniaProperty.RegisterAttached<ContextMenu, bool>("Anchored", typeof(ContextMenuPlacement));

    /// <summary>Anchors every context menu that would open at the pointer.</summary>
    public static void Use() => Control.ContextRequestedEvent.AddClassHandler<Control>(OnContextRequested);

    // A class handler runs before the control's own handlers, so before its menu opens.
    private static void OnContextRequested(Control control, ContextRequestedEventArgs e)
    {
        if (control.ContextMenu is not { } menu || menu.IsOpen
            || (menu.Placement != PlacementMode.Pointer && !menu.GetValue(AnchoredProperty)))
        {
            return;
        }
        // Avalonia places the menu against the control the request came from, so the point is taken from there too.
        if (e.TryGetPosition(e.Source as Control ?? control, out var point))
        {
            menu.SetValue(AnchoredProperty, true);
            menu.Placement = PlacementMode.AnchorAndGravity;
            menu.PlacementAnchor = PopupAnchor.TopLeft;
            menu.PlacementGravity = PopupGravity.BottomRight;
            menu.PlacementRect = new Rect(point, new Size(1, 1));
        }
        else
        {
            // From the keyboard, Avalonia opens the menu under the control, which an earlier click mustn't move.
            menu.PlacementRect = null;
        }
    }
}
