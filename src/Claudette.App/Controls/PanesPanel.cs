using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Claudette.App.Controls;

/// <summary>
/// The tabs on screen side by side (DESIGN.md §18, "Split panes"): each shown child in the order of its
/// <see cref="IndexProperty"/>, sharing the width evenly. A child that isn't shown takes no room, as the other tabs'
/// views wait out of sight. Each pane after the first draws the line between it and the one before
/// (<see cref="DividerThickness"/>).
/// </summary>
public sealed class PanesPanel : Panel
{
    /// <summary>A child's place from the left; one below 0 isn't on screen.</summary>
    public static readonly AttachedProperty<int> IndexProperty =
        AvaloniaProperty.RegisterAttached<PanesPanel, Control, int>("Index", -1);

    static PanesPanel() => AffectsParentMeasure<PanesPanel>(IndexProperty);

    public static int GetIndex(Control control) => control.GetValue(IndexProperty);

    public static void SetIndex(Control control, int value) => control.SetValue(IndexProperty, value);

    /// <summary>A pane's border: a line on its left when another pane is before it.</summary>
    public static readonly IValueConverter DividerThickness = new FuncValueConverter<int, Thickness>(index => index > 0 ? new Thickness(1, 0, 0, 0) : default);

    /// <summary>The children on screen, left to right.</summary>
    private List<Control> Shown() =>
        [.. Children.Where(c => c.IsVisible && GetIndex(c) >= 0).OrderBy(GetIndex)];

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Shown();
        var width = double.IsInfinity(availableSize.Width) || shown.Count == 0 ? availableSize.Width : availableSize.Width / shown.Count;
        var height = 0.0;
        foreach (var child in Children)
        {
            if (shown.Contains(child))
            {
                child.Measure(new Size(width, availableSize.Height));
                height = Math.Max(height, child.DesiredSize.Height);
            }
            else
            {
                child.Measure(default);
            }
        }
        return new Size(double.IsInfinity(availableSize.Width) ? shown.Sum(c => c.DesiredSize.Width) : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Shown();
        var width = shown.Count == 0 ? 0 : finalSize.Width / shown.Count;
        foreach (var child in Children)
        {
            var at = shown.IndexOf(child);
            child.Arrange(at < 0 ? default : new Rect(at * width, 0, width, finalSize.Height));
        }
        return finalSize;
    }
}
