using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Claudette.App.Controls;

/// <summary>
/// The tabs on screen side by side (DESIGN.md §18, "Split panes"): each shown child in the order of its
/// <see cref="IndexProperty"/>, sharing the width as <see cref="Shares"/> says, or evenly. A child that isn't shown takes
/// no room, as the other tabs' views wait out of sight. Each pane after the first draws the line between it and the one
/// before (<see cref="DividerThickness"/>); <see cref="Lines"/> says where those are, for dragging them.
/// </summary>
public sealed class PanesPanel : Panel
{
    /// <summary>A child's place from the left; one below 0 isn't on screen.</summary>
    public static readonly AttachedProperty<int> IndexProperty =
        AvaloniaProperty.RegisterAttached<PanesPanel, Control, int>("Index", -1);

    public static readonly StyledProperty<IReadOnlyList<double>?> SharesProperty =
        AvaloniaProperty.Register<PanesPanel, IReadOnlyList<double>?>(nameof(Shares));

    public static readonly StyledProperty<double> LeastWidthProperty =
        AvaloniaProperty.Register<PanesPanel, double>(nameof(LeastWidth), 360);

    static PanesPanel()
    {
        AffectsParentMeasure<PanesPanel>(IndexProperty);
        AffectsMeasure<PanesPanel>(SharesProperty, LeastWidthProperty);
    }

    public static int GetIndex(Control control) => control.GetValue(IndexProperty);

    public static void SetIndex(Control control, int value) => control.SetValue(IndexProperty, value);

    /// <summary>A pane's border: a line on its left when another pane is before it.</summary>
    public static readonly IValueConverter DividerThickness = new FuncValueConverter<int, Thickness>(index => index > 0 ? new Thickness(1, 0, 0, 0) : default);

    /// <summary>The share of the width each pane has, left to right; null, or of another count, for even shares.</summary>
    public IReadOnlyList<double>? Shares
    {
        get => GetValue(SharesProperty);
        set => SetValue(SharesProperty, value);
    }

    /// <summary>The least width a pane gets by its share; shares that would go under it give way to even ones.</summary>
    public double LeastWidth
    {
        get => GetValue(LeastWidthProperty);
        set => SetValue(LeastWidthProperty, value);
    }

    /// <summary>Where the lines between the panes are, from the left, after the last arrange; empty with one pane.</summary>
    public IReadOnlyList<double> Lines { get; private set; } = [];

    /// <summary>The panes have their places again: the lines between them may have moved.</summary>
    public event EventHandler? Arranged;

    /// <summary>The children on screen, left to right.</summary>
    private List<Control> Shown() =>
        [.. Children.Where(c => c.IsVisible && GetIndex(c) >= 0).OrderBy(GetIndex)];

    /// <summary>Each shown pane's width out of <paramref name="width"/>: by its share, unless that leaves one too narrow.</summary>
    private double[] Widths(int count, double width)
    {
        if (count == 0)
        {
            return [];
        }
        var even = Enumerable.Repeat(width / count, count).ToArray();
        if (Shares is not { } shares || shares.Count != count)
        {
            return even;
        }
        var widths = shares.Select(s => s * width).ToArray();
        return widths.All(w => w >= Math.Min(LeastWidth, width / count)) ? widths : even;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Shown();
        var widths = double.IsInfinity(availableSize.Width) ? null : Widths(shown.Count, availableSize.Width);
        var height = 0.0;
        foreach (var child in Children)
        {
            var at = shown.IndexOf(child);
            if (at >= 0)
            {
                child.Measure(new Size(widths?[at] ?? availableSize.Width, availableSize.Height));
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
        var widths = Widths(shown.Count, finalSize.Width);
        var lines = new List<double>();
        var x = 0.0;
        foreach (var (child, at) in shown.Select((c, i) => (c, i)))
        {
            if (at > 0)
            {
                lines.Add(x);
            }
            child.Arrange(new Rect(x, 0, widths[at], finalSize.Height));
            x += widths[at];
        }
        foreach (var child in Children.Where(c => !shown.Contains(c)))
        {
            child.Arrange(default);
        }
        Lines = lines;
        Arranged?.Invoke(this, EventArgs.Empty);
        return finalSize;
    }
}
