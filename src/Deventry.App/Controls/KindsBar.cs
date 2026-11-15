using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Deventry.Usage;

namespace Deventry.App.Controls;

/// <summary>
/// The four kinds of token, or their cost, in the order every view puts them (DESIGN.md §18, "Kinds of token"): cache read,
/// cache write, fresh input, output.
/// </summary>
public sealed record KindValues(double CacheRead, double CacheWrite, double Input, double Output)
{
    public static readonly KindValues Zero = new(0, 0, 0, 0);

    public double Total => CacheRead + CacheWrite + Input + Output;

    public static KindValues Of(TokenKinds tokens) => new(tokens.CacheRead, tokens.CacheWrite, tokens.Input, tokens.Output);

    public static KindValues Of(KindCosts costs) => new(costs.CacheRead, costs.CacheWrite, costs.Input, costs.Output);

    /// <summary>Each kind's value with its color, in order.</summary>
    internal IEnumerable<(double Value, string Brush)> Parts =>
    [
        (CacheRead, KindsBar.CacheReadBrush),
        (CacheWrite, KindsBar.CacheWriteBrush),
        (Input, KindsBar.InputBrush),
        (Output, KindsBar.OutputBrush),
    ];
}

/// <summary>
/// A stacked bar of the four kinds (DESIGN.md §18, "Token breakdown"), each as wide as its share, with a gap of the surface
/// between them and a sliver for one too small to see, as wide as <see cref="Scale"/> of the bar's room.
/// </summary>
public sealed class KindsBar : Control
{
    public const string CacheReadBrush = "TokenCacheReadBrush";
    public const string CacheWriteBrush = "TokenCacheWriteBrush";
    public const string InputBrush = "TokenInputBrush";
    public const string OutputBrush = "TokenOutputBrush";

    public static readonly StyledProperty<KindValues?> ValuesProperty =
        AvaloniaProperty.Register<KindsBar, KindValues?>(nameof(Values));

    /// <summary>How much of its room the bar takes, 0 to 1: its value against the largest beside it.</summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<KindsBar, double>(nameof(Scale), 1);

    static KindsBar()
    {
        AffectsRender<KindsBar>(ValuesProperty, ScaleProperty);
    }

    public KindsBar()
    {
        // The colors are the theme's: a change of theme draws it again.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public KindValues? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Values is not { Total: > 0 } values || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        var thin = Bounds.Height <= 6;
        var gap = thin ? 1.0 : 2.0;
        var width = Math.Max(2, Bounds.Width * Math.Clamp(Scale, 0, 1));
        var parts = values.Parts.Where(p => p.Value > 0).ToList();
        var room = width - (gap * (parts.Count - 1));
        if (room <= 0)
        {
            return;
        }
        // A part too small to see keeps a sliver, which the others make room for.
        const double sliver = 1.5;
        var widths = parts.Select(p => room * p.Value / values.Total).ToList();
        var short_ = widths.Sum(w => w < sliver ? sliver - w : 0);
        var large = widths.Where(w => w >= sliver).Sum();
        widths = [.. widths.Select(w => w < sliver ? sliver : large > 0 ? w - (short_ * w / large) : w)];

        var radius = thin ? 2.0 : 3.0;
        using (context.PushClip(new RoundedRect(new Rect(0, 0, width, Bounds.Height), radius)))
        {
            var x = 0.0;
            for (var i = 0; i < parts.Count; i++)
            {
                if (Brush(parts[i].Brush) is { } brush)
                {
                    context.FillRectangle(brush, new Rect(x, 0, Math.Max(0, widths[i]), Bounds.Height));
                }
                x += widths[i] + gap;
            }
        }
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var found) ? found as IBrush : null;
}
