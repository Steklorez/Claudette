using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Deventry.App.Controls;

/// <summary>
/// Behind a working tab's row in the sidebar (DESIGN.md §4, "Sidebar"): a soft highlight that runs across it from left
/// to right and starts again, about every two seconds, while Claude works; faint, so the row's text reads over it. With
/// motion reduced it holds still as a wash fading out to the right, as the other states' do.
/// </summary>
public sealed class WorkBar : Control
{
    public static readonly StyledProperty<IBrush?> BarBrushProperty = AvaloniaProperty.Register<WorkBar, IBrush?>(nameof(BarBrush));

    public static readonly StyledProperty<bool> IsStillProperty = AvaloniaProperty.Register<WorkBar, bool>(nameof(IsStill));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<WorkBar>();

    /// <summary>How long the highlight takes to run along the line once.</summary>
    private static readonly TimeSpan Lap = TimeSpan.FromSeconds(2.2);

    /// <summary>The highlight's width, as a share of the line's.</summary>
    private const double Glow = 0.6;

    private bool _animating;
    private double _phase;
    private TimeSpan? _start;

    static WorkBar()
    {
        AffectsRender<WorkBar>(BarBrushProperty, IsStillProperty, CornerRadiusProperty);
    }

    /// <summary>The highlight's color; the line under it is the same, faint.</summary>
    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>Its row's corners, so it fills the row as the row's hover does.</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>No running highlight: Settings → Appearance → Motion, or the system, asks for less motion.</summary>
    public bool IsStill
    {
        get => GetValue(IsStillProperty);
        set => SetValue(IsStillProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == IsStillProperty)
        {
            Animate();
        }
    }

    /// <summary>Asks for frames while the line shows and moves; each frame asks for the next, so it stops by itself.</summary>
    private void Animate()
    {
        if (_animating || !IsVisible || IsStill || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _animating = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        _animating = false;
        if (!IsVisible || IsStill || TopLevel.GetTopLevel(this) is null)
        {
            _start = null;
            return;
        }
        _start ??= time;
        _phase = (time - _start.Value).TotalMilliseconds / Lap.TotalMilliseconds % 1;
        InvalidateVisual();
        Animate();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0 || BarBrush is not ISolidColorBrush { Color: var color })
        {
            return;
        }
        var rect = new RoundedRect(bounds, CornerRadius);
        const double peak = 0.22;
        Color Faded(double alpha) => Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);
        if (IsStill)
        {
            var wash = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Faded(peak), 0), new GradientStop(Faded(0), 1) },
            };
            context.DrawRectangle(wash, null, rect);
            return;
        }
        context.DrawRectangle(new SolidColorBrush(color, 0.05), null, rect);
        // The highlight: a soft band centred on the phase, coming in again at the left as it leaves at the right.
        var centre = _phase * (1 + Glow) - Glow / 2;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Faded(0), Math.Clamp(centre - Glow / 2, 0, 1)),
                new GradientStop(Faded(peak), Math.Clamp(centre, 0, 1)),
                new GradientStop(Faded(0), Math.Clamp(centre + Glow / 2, 0, 1)),
            },
        };
        context.DrawRectangle(brush, null, rect);
    }
}
