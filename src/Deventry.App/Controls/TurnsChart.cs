using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Deventry.Core;

namespace Deventry.App.Controls;

/// <summary>What a turn's column is marked with (DESIGN.md §18, "The window").</summary>
public enum TurnMark
{
    None,

    /// <summary>Its first call found the prompt cache expired: a dot in the caution color.</summary>
    CacheExpired,

    /// <summary>It compacted the conversation first: a chevron down.</summary>
    Compacted,

    /// <summary>A subagent worked in it: a diamond.</summary>
    Subagent,
}

/// <summary>A turn's column in a <see cref="TurnsChart"/>.</summary>
/// <param name="Label">Under its number: the time it ended, *11:32*.</param>
/// <param name="Context">How full the main agent's context was after it; null without a call.</param>
/// <param name="Tip">What its tooltip says.</param>
public sealed record TurnColumn(int Number, string Label, KindValues Values, TurnMark Mark, long? Context, string Tip);

/// <summary>
/// The token breakdown's turn by turn (DESIGN.md §18, "The window"): a column per turn with its kinds stacked, in tokens or
/// cost, its number and time under it and its mark over it; under them, on an axis of its own, the context after each turn
/// with the auto-compact threshold dashed. A click selects the turn. Drawn here, with no charting package, in the theme's
/// colors, as the usage charts are (DESIGN.md §6, "Detailed header").
/// </summary>
public sealed class TurnsChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<TurnColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<TurnsChart, IReadOnlyList<TurnColumn>?>(nameof(Columns));

    public static readonly StyledProperty<int> SelectedNumberProperty =
        AvaloniaProperty.Register<TurnsChart, int>(nameof(SelectedNumber));

    /// <summary>The columns are cost, so the axis is in dollars; otherwise tokens.</summary>
    public static readonly StyledProperty<bool> IsCostProperty =
        AvaloniaProperty.Register<TurnsChart, bool>(nameof(IsCost));

    /// <summary>The context window's size, the context line's top; null hides the line.</summary>
    public static readonly StyledProperty<long?> ContextWindowProperty =
        AvaloniaProperty.Register<TurnsChart, long?>(nameof(ContextWindow));

    /// <summary>Where Claude Code compacts by itself, dashed across the context line; null when it isn't known.</summary>
    public static readonly StyledProperty<long?> AutoCompactAtProperty =
        AvaloniaProperty.Register<TurnsChart, long?>(nameof(AutoCompactAt));

    /// <summary>Run with a turn's number when its column is clicked.</summary>
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<TurnsChart, ICommand?>(nameof(SelectCommand));

    private const double AxisWidth = 48;
    private const double LabelsHeight = 30;
    private const double MarkRoom = 14;
    private const double ContextHeight = 86;
    private const double ContextGap = 14;

    static TurnsChart()
    {
        AffectsRender<TurnsChart>(ColumnsProperty, SelectedNumberProperty, IsCostProperty, ContextWindowProperty, AutoCompactAtProperty);
    }

    public TurnsChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<TurnColumn>? Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    public int SelectedNumber { get => GetValue(SelectedNumberProperty); set => SetValue(SelectedNumberProperty, value); }

    public bool IsCost { get => GetValue(IsCostProperty); set => SetValue(IsCostProperty, value); }

    public long? ContextWindow { get => GetValue(ContextWindowProperty); set => SetValue(ContextWindowProperty, value); }

    public long? AutoCompactAt { get => GetValue(AutoCompactAtProperty); set => SetValue(AutoCompactAtProperty, value); }

    public ICommand? SelectCommand { get => GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }

    private bool ShowsContext => ContextWindow is > 0 && (Columns ?? []).Any(c => c.Context is not null);

    /// <summary>Where the columns are drawn, above their labels.</summary>
    private Rect Plot => new(AxisWidth, MarkRoom, Math.Max(0, Bounds.Width - AxisWidth - 4),
        Math.Max(0, Bounds.Height - MarkRoom - LabelsHeight - (ShowsContext ? ContextHeight + ContextGap : 0)));

    /// <summary>The column under <paramref name="x"/>, or null.</summary>
    private TurnColumn? ColumnAt(double x)
    {
        var columns = Columns ?? [];
        var plot = Plot;
        if (columns.Count == 0 || plot.Width <= 0 || x < plot.Left || x >= plot.Right)
        {
            return null;
        }
        return columns[Math.Clamp((int)((x - plot.Left) / (plot.Width / columns.Count)), 0, columns.Count - 1)];
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var tip = ColumnAt(e.GetPosition(this).X)?.Tip;
        if (!Equals(ToolTip.GetTip(this), tip))
        {
            ToolTip.SetTip(this, tip);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ColumnAt(e.GetPosition(this).X) is { } column && SelectCommand is { } command
            && command.CanExecute(column.Number))
        {
            command.Execute(column.Number);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var columns = Columns ?? [];
        var plot = Plot;
        if (columns.Count == 0 || plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }
        var grid = new Pen(Brush("DividerBrush"), 1);
        var labels = Brush("MutedTextBrush");
        var text = GetValue(TextElement.ForegroundProperty) ?? labels;

        // The axis: whole steps, so its labels are round.
        var largest = columns.Max(c => c.Values.Total);
        var step = NiceStep(Math.Max(largest, IsCost ? 0.01 : 1) / 4);
        var top = step * Math.Ceiling(Math.Max(largest, step) / step);
        var steps = (int)Math.Round(top / step);
        for (var i = 0; i <= steps; i++)
        {
            var y = plot.Bottom - (plot.Height * i / steps);
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, AxisLabel(step * i), new Point(0, y - 7), AxisWidth - 6, TextAlignment.Right, labels);
        }

        var slot = plot.Width / columns.Count;
        var width = Math.Min(slot * 0.62, 34);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var x = plot.Left + (slot * i) + ((slot - width) / 2);
            var y = plot.Bottom;
            foreach (var (value, key) in column.Values.Parts.Where(p => p.Value > 0))
            {
                var height = Math.Max(plot.Height * value / top, 0.75);
                y -= height;
                if (Brush(key) is { } fill)
                {
                    // A line of the surface between the kinds, as the bars have.
                    context.FillRectangle(fill, new Rect(x, y, width, Math.Max(0.5, height - (height > 3 ? 1.5 : 0))));
                }
            }
            DrawMark(context, column.Mark, new Point(x + (width / 2), y - 7));
            if (column.Number == SelectedNumber && Brush("MeterBrush") is { } accent)
            {
                context.DrawRectangle(null, new Pen(accent, 1), new RoundedRect(new Rect(plot.Left + (slot * i) + 1, plot.Top - MarkRoom + 2, slot - 2, plot.Height + MarkRoom), 4));
            }
            if (slot >= 16 || i % (int)Math.Ceiling(16 / slot) == 0)
            {
                DrawText(context, column.Number.ToString(CultureInfo.CurrentCulture), new Point(x + (width / 2) - 20, plot.Bottom + 3), 40, TextAlignment.Center, text);
                if (slot >= 34)
                {
                    DrawText(context, column.Label, new Point(x + (width / 2) - 24, plot.Bottom + 16), 48, TextAlignment.Center, labels);
                }
            }
        }

        if (ShowsContext && ContextWindow is { } window)
        {
            DrawContext(context, columns, plot, slot, window, labels, text);
        }
    }

    /// <summary>The context after each turn, on an axis of its own under the columns, with the auto-compact threshold.</summary>
    private void DrawContext(DrawingContext context, IReadOnlyList<TurnColumn> columns, Rect plot, double slot, long window, IBrush? labels, IBrush? text)
    {
        var area = new Rect(plot.Left, plot.Bottom + LabelsHeight + ContextGap + 12, plot.Width, ContextHeight - 16);
        var grid = new Pen(Brush("DividerBrush"), 1);
        DrawText(context, "Context after each turn", new Point(plot.Left, area.Top - 16), 220, TextAlignment.Left, text);
        double Y(double tokens) => area.Bottom - (area.Height * Math.Min(1, tokens / window));
        foreach (var level in (double[])[0, window / 2.0, window])
        {
            context.DrawLine(grid, new Point(area.Left, Y(level)), new Point(area.Right, Y(level)));
            DrawText(context, Formats.Tokens((long)level), new Point(0, Y(level) - 7), AxisWidth - 6, TextAlignment.Right, labels);
        }
        if (AutoCompactAt is { } threshold && threshold < window && Brush("MeterWarningBrush") is { } caution)
        {
            context.DrawLine(new Pen(caution, 1, new DashStyle([4, 3], 0)), new Point(area.Left, Y(threshold)), new Point(area.Right, Y(threshold)));
            DrawText(context, $"auto-compacts at {Formats.Tokens(threshold)}", new Point(area.Right - 180, Y(threshold) - 15), 180, TextAlignment.Right, caution);
        }
        if (text is null)
        {
            return;
        }
        var line = new Pen(text, 2, lineJoin: PenLineJoin.Round);
        Point? previous = null;
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Context is not { } tokens)
            {
                previous = null;
                continue;
            }
            var point = new Point(plot.Left + (slot * i) + (slot / 2), Y(tokens));
            if (previous is { } from)
            {
                context.DrawLine(line, from, point);
            }
            context.DrawEllipse(text, null, point, 3, 3);
            previous = point;
        }
    }

    private void DrawMark(DrawingContext context, TurnMark mark, Point center)
    {
        switch (mark)
        {
            case TurnMark.CacheExpired when Brush("MeterWarningBrush") is { } caution:
                context.DrawEllipse(caution, null, center, 3.5, 3.5);
                break;
            case TurnMark.Compacted when Brush("MutedTextBrush") is { } muted:
                var pen = new Pen(muted, 1.5, lineCap: PenLineCap.Round);
                context.DrawLine(pen, new Point(center.X - 4, center.Y - 2), new Point(center.X, center.Y + 2));
                context.DrawLine(pen, new Point(center.X, center.Y + 2), new Point(center.X + 4, center.Y - 2));
                break;
            case TurnMark.Subagent when Brush("MutedTextBrush") is { } muted:
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(center.X, center.Y - 4), true);
                    g.LineTo(new Point(center.X + 4, center.Y));
                    g.LineTo(new Point(center.X, center.Y + 4));
                    g.LineTo(new Point(center.X - 4, center.Y));
                    g.EndFigure(true);
                }
                context.DrawGeometry(null, new Pen(muted, 1.2), diamond);
                break;
        }
    }

    private string AxisLabel(double value) => IsCost
        ? value == 0 ? "$0" : value < 1 ? string.Create(CultureInfo.InvariantCulture, $"${value:0.##}") : string.Create(CultureInfo.InvariantCulture, $"${value:0.#}")
        : Formats.Tokens((long)Math.Round(value));

    /// <summary>A round step at least <paramref name="value"/>: 1, 2, 2.5 or 5 times a power of ten.</summary>
    internal static double NiceStep(double value)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var multiple in (double[])[1, 2, 2.5, 5, 10])
        {
            if (multiple * power >= value)
            {
                return multiple * power;
            }
        }
        return value;
    }

    private void DrawText(DrawingContext context, string text, Point origin, double width, TextAlignment alignment, IBrush? brush)
    {
        if (brush is null)
        {
            return;
        }
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush)
        {
            MaxTextWidth = width,
            TextAlignment = alignment,
            MaxLineCount = 1,
        }, origin);
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var found) ? found as IBrush : null;
}
