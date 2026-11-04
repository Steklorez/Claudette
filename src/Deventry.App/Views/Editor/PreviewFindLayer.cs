using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Deventry.App.Controls;
using Deventry.App.Editor;

namespace Deventry.App.Views.Editor;

/// <summary>
/// The matches of find in a Markdown file's preview (DESIGN.md §18, "Markdown preview", "Finding in the preview"): a
/// layer over the preview that marks each in the find color, as the editor's find marks its text, and the one it's on
/// with an outline in the accent too. It finds them in the text as the preview's blocks laid it out, as the conversation's
/// find does (<see cref="FindHighlights"/>), when what's looked for or what the preview shows changes, and works out where
/// they are after each layout pass, so the marks follow scrolling and a narrower pane. It brings the one it's on into view.
/// </summary>
public sealed class PreviewFindLayer : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<PreviewFindLayer, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> CurrentProperty = AvaloniaProperty.Register<PreviewFindLayer, IBrush?>(nameof(Current));

    private readonly List<(Rect Rect, bool IsCurrent)> _marks = [];
    private PreviewFind? _find;
    private Control? _root;
    private ScrollViewer? _scroller;
    private List<TextBlock> _blocks = [];
    private IReadOnlyList<PreviewMatch> _matches = [];

    /// <summary>The matches are to be found again, at the next layout pass.</summary>
    private bool _stale = true;

    /// <summary>The match it's on is to be brought into view, once found.</summary>
    private bool _reveal;

    static PreviewFindLayer() => AffectsRender<PreviewFindLayer>(FillProperty, CurrentProperty);

    public PreviewFindLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        LayoutUpdated += (_, _) => Update();
    }

    /// <summary>Every match's color, over the text.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The outline of the match it's on.</summary>
    public IBrush? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    /// <summary>Where each mark is, in this layer, and which is the one it's on: for tests.</summary>
    internal IReadOnlyList<(Rect Rect, bool IsCurrent)> Marks => _marks;

    /// <summary>
    /// Marks <paramref name="find"/>'s matches in the text blocks under <paramref name="root"/>, scrolling
    /// <paramref name="scroller"/> to the one it's on.
    /// </summary>
    public void Attach(PreviewFind? find, Control root, ScrollViewer scroller)
    {
        _scroller = scroller;
        if (_find is not null)
        {
            _find.QueryChanged -= OnQueryChanged;
            _find.CurrentChanged -= OnCurrentChanged;
        }
        _find = find;
        _root = root;
        if (_find is not null)
        {
            _find.QueryChanged += OnQueryChanged;
            _find.CurrentChanged += OnCurrentChanged;
        }
        OnQueryChanged();
    }

    /// <summary>What the preview shows changed: its matches are found again.</summary>
    public void ContentChanged()
    {
        _stale = true;
        InvalidateArrange();
    }

    private void OnQueryChanged()
    {
        _stale = true;
        _reveal = true;
        InvalidateArrange();
    }

    private void OnCurrentChanged()
    {
        _reveal = true;
        InvalidateArrange();
    }

    private void Update()
    {
        if (_find is not { IsOpen: true, Matcher: { } matcher } find || _root is null || !IsEffectivelyVisible)
        {
            if (_marks.Count > 0)
            {
                _marks.Clear();
                InvalidateVisual();
            }
            return;
        }
        if (_stale)
        {
            _stale = false;
            _blocks = [.. _root.GetVisualDescendants().OfType<TextBlock>().Where(b => b.IsEffectivelyVisible)];
            _matches = PreviewFind.Find([.. _blocks.Select(b => FindHighlights.LaidOutText(b.TextLayout))], matcher);
            find.Found(_matches.Count);
        }
        var marks = new List<(Rect, bool)>();
        var shown = new Rect(Bounds.Size);
        for (var i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            if (match.Block >= _blocks.Count || _blocks[match.Block] is not { } block || !block.IsAttachedToVisualTree())
            {
                continue;
            }
            foreach (var rect in RectsOf(block, match))
            {
                if (block.TranslatePoint(rect.TopLeft, this) is { } topLeft && block.TranslatePoint(rect.BottomRight, this) is { } bottomRight
                    && new Rect(topLeft, bottomRight).Intersect(shown) is { Width: > 0, Height: > 0 } mark)
                {
                    marks.Add((mark, i == find.Current));
                }
            }
        }
        if (_reveal && find.Current >= 0 && find.Current < _matches.Count)
        {
            _reveal = false;
            var current = _matches[find.Current];
            if (current.Block < _blocks.Count && RectsOf(_blocks[current.Block], current).FirstOrDefault() is { Width: > 0 } at)
            {
                Reveal(_blocks[current.Block], at);
            }
        }
        if (!marks.SequenceEqual(_marks))
        {
            _marks.Clear();
            _marks.AddRange(marks);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Scrolls the preview so <paramref name="rect"/>, in <paramref name="block"/>, is in view, a third of the way down,
    /// when it isn't already.
    /// </summary>
    private void Reveal(TextBlock block, Rect rect)
    {
        if (_scroller is not { Content: Visual content } scroller || block.TranslatePoint(rect.TopLeft, content) is not { } at)
        {
            return;
        }
        var (view, height) = (scroller.Offset.Y, scroller.Viewport.Height);
        if (at.Y >= view && at.Y + rect.Height <= view + height)
        {
            return;
        }
        scroller.Offset = scroller.Offset.WithY(Math.Clamp(at.Y - height / 3, 0, Math.Max(0, scroller.Extent.Height - height)));
    }

    /// <summary>
    /// Where a match's text is laid out in its block: one rect for each line it's on; none when the block's text has
    /// changed under it, until the matches are found again.
    /// </summary>
    private static IEnumerable<Rect> RectsOf(TextBlock block, PreviewMatch match)
    {
        var layout = block.TextLayout;
        if (layout.TextLines.Count == 0 || match.Start + match.Length > layout.TextLines[^1].FirstTextSourceIndex + layout.TextLines[^1].Length)
        {
            return [];
        }
        var origin = new Point(block.Padding.Left, block.Padding.Top);
        return layout.HitTestTextRange(match.Start, match.Length).Select(r => r.Translate(origin));
    }

    public override void Render(DrawingContext context)
    {
        var pen = Current is { } accent ? new Pen(accent, 1.5) : null;
        foreach (var (rect, isCurrent) in _marks)
        {
            context.DrawRectangle(Fill, isCurrent ? pen : null, rect, 2, 2);
        }
    }
}
