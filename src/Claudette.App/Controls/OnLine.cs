using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Claudette.App.Controls;

/// <summary>
/// Centers a control on the line an ancestor border draws down its left edge (<see cref="LineProperty"/>, the border's
/// name), such as a tab row's chevron on its group's line (DESIGN.md §4, "Sidebar"). The control stays where its layout
/// puts it, which can be anywhere along the row; after each layout pass it's moved across onto the line, on whole screen
/// pixels, with a render transform, so its layout, and the row's, stay as they were. Working it out from the line
/// itself keeps it centered however far in the row is, and at any scale: margins counted from the row's own padding
/// round to screen pixels one by one, and at 150% end up a pixel off.
/// </summary>
public static class OnLine
{
    /// <summary>The name of the ancestor border whose left edge is the line.</summary>
    public static readonly AttachedProperty<string?> LineProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Line", typeof(OnLine));

    static OnLine()
    {
        LineProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.LayoutUpdated -= OnLayoutUpdated;
            if (e.GetNewValue<string?>() is not null)
            {
                control.LayoutUpdated += OnLayoutUpdated;
            }
            else
            {
                control.RenderTransform = null;
            }
        });
    }

    public static string? GetLine(Control element) => element.GetValue(LineProperty);

    public static void SetLine(Control element, string? value) => element.SetValue(LineProperty, value);

    /// <summary>How far across the control is moved, in pixels: left when it's negative.</summary>
    internal static double ShiftOf(Control control) => control.RenderTransform is TranslateTransform shift ? shift.X : 0;

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not Control control || GetLine(control) is not { } name || !control.IsEffectivelyVisible
            || control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Name == name) is not { } line
            || TopLevel.GetTopLevel(control) is not { } top)
        {
            return;
        }
        var scale = top.RenderScaling * (control.TransformToVisual(top)?.M11 is > 0 and var zoom ? zoom : 1);
        // The line as it's drawn: its thickness on whole screen pixels, as the border draws it.
        var thickness = Math.Round(line.BorderThickness.Left * scale) / scale;
        // Measured where the control is drawn, already moved by the shift it has now.
        if (line.TranslatePoint(new Point(thickness / 2, 0), control) is not { } middle)
        {
            return;
        }
        var current = ShiftOf(control);
        var shift = Math.Round((current + middle.X - control.Bounds.Width / 2) * scale) / scale;
        if (Math.Abs(shift - current) > 0.01)
        {
            control.RenderTransform = shift == 0 ? null : new TranslateTransform(shift, 0);
        }
    }
}
