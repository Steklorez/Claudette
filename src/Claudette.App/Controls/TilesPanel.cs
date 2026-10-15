using Avalonia;
using Avalonia.Controls;

namespace Claudette.App.Controls;

/// <summary>
/// Lays a Tool calls or Modified files card's tiles out in rows (DESIGN.md §18, "Conversation cards"): as many columns
/// as fit at <see cref="TileWidth"/> each, up to <see cref="MaxColumns"/>, sharing the width evenly. A row is as tall as
/// its tallest tile, and every tile in it takes that height, so their edges line up.
/// </summary>
public sealed class TilesPanel : Panel
{
    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<TilesPanel, double>(nameof(TileWidth), 240);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<TilesPanel, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TilesPanel, double>(nameof(Spacing), 6);

    static TilesPanel() => AffectsMeasure<TilesPanel>(TileWidthProperty, MaxColumnsProperty, SpacingProperty);

    /// <summary>The least width a tile gets before the row has one column fewer.</summary>
    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>The gap between tiles, across and down.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many columns fit in <paramref name="width"/>.</summary>
    internal int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0)
        {
            return 1;
        }
        var fit = (int)((width + Spacing) / (TileWidth + Spacing));
        return Math.Clamp(fit, 1, Math.Max(1, MaxColumns));
    }

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Children.Where(c => c.IsVisible).ToList();
        var columns = ColumnsFor(availableSize.Width);
        var columnWidth = double.IsInfinity(availableSize.Width) ? TileWidth : ColumnWidth(availableSize.Width, columns);
        foreach (var tile in shown)
        {
            tile.Measure(new Size(columnWidth, double.PositiveInfinity));
        }
        var rows = RowHeights(shown, columns);
        var total = rows.Sum() + Spacing * Math.Max(0, rows.Count - 1);
        var width = double.IsInfinity(availableSize.Width)
            ? columnWidth * Math.Min(columns, Math.Max(1, shown.Count)) + Spacing * (Math.Min(columns, Math.Max(1, shown.Count)) - 1)
            : availableSize.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(c => c.IsVisible).ToList();
        var columns = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, columns);
        var rows = RowHeights(shown, columns);
        var y = 0.0;
        for (int i = 0, row = 0; i < shown.Count; i += columns, row++)
        {
            var height = rows[row];
            var x = 0.0;
            foreach (var tile in shown.Skip(i).Take(columns))
            {
                tile.Arrange(new Rect(x, y, columnWidth, height));
                x += columnWidth + Spacing;
            }
            y += height + Spacing;
        }
        return finalSize;
    }

    /// <summary>Each row's height: its tallest tile's.</summary>
    private static List<double> RowHeights(List<Control> shown, int columns)
    {
        var rows = new List<double>();
        for (var i = 0; i < shown.Count; i += columns)
        {
            rows.Add(shown.Skip(i).Take(columns).Max(t => t.DesiredSize.Height));
        }
        return rows;
    }
}
