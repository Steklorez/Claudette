using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace Claudette.App.Controls;

/// <summary>
/// Where Find's text is in the card it brought into view (DESIGN.md §5, "Find"): a layer over the conversation that marks
/// each place the search text appears in the card's text, over the text itself. It reads the text as each text block laid
/// it out, so a reply's Markdown, a code block and a tool's output are found alike, and asks the block's layout where
/// those characters are. Each mark is kept to what its block shows, so text scrolled away inside a tool's output isn't
/// marked over the cards around it. The marks follow the conversation as it scrolls and lays out again.
/// </summary>
public sealed class FindHighlights : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<FindHighlights, IBrush?>(nameof(Fill));

    private IReadOnlyList<Rect> _rects = [];
    private Func<Control?>? _target;
    private string _query = "";

    static FindHighlights() => AffectsRender<FindHighlights>(FillProperty);

    public FindHighlights()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        // Scrolling, a card opening or a reply growing all end in a layout pass.
        LayoutUpdated += (_, _) => Update();
    }

    /// <summary>The marks' color, over the text.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Where each mark is, in this layer: for tests.</summary>
    internal IReadOnlyList<Rect> Rects => _rects;

    /// <summary>
    /// Marks <paramref name="query"/> in the card <paramref name="target"/> finds, asked again at each update since the
    /// conversation's cards are made and recycled as it scrolls. No query, or no card, marks nothing.
    /// </summary>
    public void Show(Func<Control?>? target, string query)
    {
        _target = target;
        _query = query;
        Update();
    }

    /// <summary>Works out the marks again, and draws them if they moved.</summary>
    public void Update()
    {
        var rects = _query.Length == 0 || _target?.Invoke() is not { } card ? [] : Find(card, _query);
        if (!rects.SequenceEqual(_rects))
        {
            _rects = rects;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Fill is not { } fill)
        {
            return;
        }
        foreach (var rect in _rects)
        {
            context.DrawRectangle(fill, null, rect, 2, 2);
        }
    }

    private List<Rect> Find(Control card, string query)
    {
        var rects = new List<Rect>();
        foreach (var block in card.GetVisualDescendants().OfType<TextBlock>().Prepend(card as TextBlock).OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || block.Bounds.Width <= 0)
            {
                continue;
            }
            var layout = block.TextLayout;
            var text = LaidOutText(layout);
            if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 || Shown(block) is not { } shown)
            {
                continue;
            }
            var origin = new Point(block.Padding.Left, block.Padding.Top);
            for (var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var hit in layout.HitTestTextRange(at, query.Length))
                {
                    if (block.TranslatePoint(hit.TopLeft + origin, this) is { } topLeft && block.TranslatePoint(hit.BottomRight + origin, this) is { } bottomRight
                        && new Rect(topLeft, bottomRight).Intersect(shown) is { Width: > 0, Height: > 0 } mark)
                    {
                        rects.Add(mark);
                    }
                }
            }
        }
        return rects;
    }

    /// <summary>
    /// The text as <paramref name="layout"/> laid it out, each character at the index its hit tests use: a run without
    /// text of its own, such as an inline control or a line's end, stands as placeholders the query can't match.
    /// </summary>
    private static string LaidOutText(TextLayout layout)
    {
        if (layout.TextLines.Count == 0)
        {
            return "";
        }
        var last = layout.TextLines[^1];
        var chars = new char[last.FirstTextSourceIndex + last.Length];
        Array.Fill(chars, '￼');
        foreach (var line in layout.TextLines)
        {
            var at = line.FirstTextSourceIndex;
            foreach (var run in line.TextRuns)
            {
                var runText = run.Text.Span;
                if (runText.Length > 0 && at >= 0 && at + runText.Length <= chars.Length)
                {
                    runText.CopyTo(chars.AsSpan(at));
                }
                at += run.Length;
            }
        }
        return new string(chars);
    }

    /// <summary>
    /// What of <paramref name="block"/> can be seen, in this layer: this layer's bounds, less what any ancestor that clips
    /// cuts off, such as the scroll viewer around a tool's output. Null when none of it can.
    /// </summary>
    private Rect? Shown(Visual block)
    {
        var shown = new Rect(Bounds.Size);
        for (var visual = block.GetVisualParent(); visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
        {
            if (visual.ClipToBounds
                && visual.TranslatePoint(default, this) is { } topLeft
                && visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), this) is { } bottomRight)
            {
                shown = shown.Intersect(new Rect(topLeft, bottomRight));
                if (shown.Width <= 0 || shown.Height <= 0)
                {
                    return null;
                }
            }
        }
        return shown;
    }
}
