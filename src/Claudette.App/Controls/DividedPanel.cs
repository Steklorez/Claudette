using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace Claudette.App.Controls;

/// <summary>
/// A vertical stack whose shown children have a thin line between each two: a Settings section's rows (DESIGN.md §14).
/// It says which children have a line above them (<see cref="HasDividerAboveProperty"/>), and a <see cref="SettingRow"/>
/// draws it. A hidden row takes no line, so the lines stay between the rows that show, whichever those are.
/// </summary>
public sealed class DividedPanel : StackPanel
{
    /// <summary>Set on each child: there's a shown child before it, so a line goes between them.</summary>
    public static readonly AttachedProperty<bool> HasDividerAboveProperty =
        AvaloniaProperty.RegisterAttached<DividedPanel, Control, bool>("HasDividerAbove");

    public static bool GetHasDividerAbove(Control control) => control.GetValue(HasDividerAboveProperty);

    public static void SetHasDividerAbove(Control control, bool value) => control.SetValue(HasDividerAboveProperty, value);

    protected override Size ArrangeOverride(Size finalSize)
    {
        // A child shown or hidden moves the lines. The line is drawn by opacity, so this never changes the layout.
        var first = true;
        foreach (var child in Children)
        {
            var divided = child.IsVisible && !first;
            SetHasDividerAbove(child, divided);
            // A row made from an item (a project type's, say) is in a presenter: the row draws the line.
            if (child is ContentPresenter { Child: { } row })
            {
                SetHasDividerAbove(row, divided);
            }
            if (child.IsVisible)
            {
                first = false;
            }
        }
        return base.ArrangeOverride(finalSize);
    }
}
