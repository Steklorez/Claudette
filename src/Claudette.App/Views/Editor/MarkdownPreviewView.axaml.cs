using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Claudette.App.Editor;
using Claudette.App.ViewModels;
using LiveMarkdown.Avalonia;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Claudette.App.Views.Editor;

/// <summary>
/// A Markdown file's preview (DESIGN.md §18, "Markdown preview"): LiveMarkdown's renderer in a scroller, Copy on its
/// code blocks, a heading a link named, and the line at its top. It keeps that line where it is as the preview changes
/// (an edit above it, an image loading, a narrower pane), and tells its file's view when the user scrolls, so the editor
/// beside it can follow.
/// </summary>
public partial class MarkdownPreviewView : UserControl
{
    private ProjectFileViewModel? _file;

    /// <summary>The place for each line, from the blocks as last laid out; null until it's needed again.</summary>
    private PreviewScroll? _map;

    /// <summary>The preview changed or moved since the map was made: it's made again after the next layout.</summary>
    private bool _changed;

    /// <summary>The line kept at the top: where the editor beside it is, or where the user scrolled to.</summary>
    private double? _topLine;

    /// <summary>An offset set here, whose scroll isn't the user's.</summary>
    private double? _settingOffset;

    public MarkdownPreviewView()
    {
        InitializeComponent();
        CodeBlockCopy.Attach(this);
        Scroller.ScrollChanged += OnScrollChanged;
        // A narrower pane, an image loading or a code block wrapping moves what's under the top. Scrolling moves the page
        // too, but keeps its size.
        Page.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && e.GetOldAndNewValue<Rect>() is var (before, after) && before.Size != after.Size)
            {
                _changed = true;
            }
        };
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>The user scrolled the preview: the line now at its top.</summary>
    public event Action<double>? Scrolled;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_file is not null)
        {
            _file.Markdown.Shown -= OnShown;
            _file.HeadingRequested -= ScrollToHeading;
        }
        _file = DataContext as ProjectFileViewModel;
        if (_file is not null)
        {
            _file.Markdown.Shown += OnShown;
            _file.HeadingRequested += ScrollToHeading;
        }
        _map = null;
        _topLine = null;
    }

    private void OnShown() => _changed = true;

    /// <summary>Once what changed has laid out, the map is made again and the line at the top kept there.</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // Out of sight, it waits until it's shown again.
        if (!_changed || !IsEffectivelyVisible)
        {
            return;
        }
        _changed = false;
        _map = null;
        if (_topLine is { } line)
        {
            SetOffset(OffsetFor(line));
        }
    }

    /// <summary>The map, once the preview is on screen and laid out; out of sight its blocks have no places.</summary>
    private PreviewScroll Map => IsEffectivelyVisible ? _map ??= MakeMap() : PreviewScroll.None;

    /// <summary>Each block of text the preview shows: the lines of the file it came from, and where it is.</summary>
    private PreviewScroll MakeMap()
    {
        if (_file is null)
        {
            return PreviewScroll.None;
        }
        var markdown = _file.Markdown;
        var blocks = new List<PreviewScroll.Block>();
        foreach (var text in Renderer.GetVisualDescendants().OfType<MarkdownTextBlock>())
        {
            var span = text.SourceSpan;
            if (span.IsEmpty || !text.IsEffectivelyVisible || text.TranslatePoint(default, Page) is not { } at)
            {
                continue;
            }
            blocks.Add(new PreviewScroll.Block(markdown.LineAt(span.Start), markdown.LineAt(span.End), at.Y, text.Bounds.Height));
        }
        return new PreviewScroll(blocks);
    }

    /// <summary>The line at the top of the preview: where it was put, or scrolled to.</summary>
    public double TopLine => _topLine ?? 1;

    /// <summary>
    /// Puts <paramref name="line"/> at the top, and keeps it there as the preview changes, until the user scrolls: the
    /// editor beside it scrolled. Before the preview has laid out what changed, it goes there once it has.
    /// </summary>
    public void ScrollToLine(double line)
    {
        _topLine = line;
        if (_changed || !IsEffectivelyVisible)
        {
            _changed = true;
            return;
        }
        SetOffset(OffsetFor(line));
    }

    /// <summary>
    /// Puts <paramref name="line"/> at the top once the preview has laid out: it's coming into view, or what it shows
    /// is still on its way.
    /// </summary>
    public void ShowLine(double line)
    {
        _topLine = line;
        _changed = true;
        InvalidateMeasure();
    }

    private double OffsetFor(double line) => Math.Clamp(Map.OffsetFor(line), 0, Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height));

    private void SetOffset(double y)
    {
        if (Math.Abs(Scroller.Offset.Y - y) < 0.5)
        {
            return;
        }
        _settingOffset = y;
        Scroller.Offset = Scroller.Offset.WithY(y);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y == 0)
        {
            return;
        }
        if (_settingOffset is { } expected && Math.Abs(Scroller.Offset.Y - expected) < 1)
        {
            _settingOffset = null;
            return;
        }
        _settingOffset = null;
        // What changed moved it, not the user; once laid out, the line at the top goes back there.
        if (_changed)
        {
            return;
        }
        var line = Map.LineFor(Scroller.Offset.Y);
        _topLine = line;
        Scrolled?.Invoke(line);
    }

    /// <summary>
    /// A link to a heading in the file: the preview scrolls to it, as if the user had, so the editor beside it follows.
    /// Its name is the one Markdig gives it.
    /// </summary>
    private void ScrollToHeading(string id)
    {
        if (Renderer.DocumentUpdate?.Document is not { } document
            || document.Descendants<HeadingBlock>().FirstOrDefault(h => string.Equals(h.GetAttributes().Id, id, StringComparison.OrdinalIgnoreCase)) is not { } heading
            || Renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().FirstOrDefault(t => t.SourceSpan.Start == heading.Span.Start) is not { } text
            || text.TranslatePoint(default, Page) is not { } at)
        {
            return;
        }
        Scroller.Offset = Scroller.Offset.WithY(Math.Clamp(at.Y, 0, Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height)));
    }
}
