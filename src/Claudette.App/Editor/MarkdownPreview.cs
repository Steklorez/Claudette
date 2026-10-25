using CommunityToolkit.Mvvm.ComponentModel;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Editor;

/// <summary>
/// A Markdown file's preview (DESIGN.md §18, "Markdown preview"): what its renderer shows, as <see cref="Conversation.KeptMarkdown"/>
/// is a reply's. The editor's text is parsed away from the UI thread and handed on as a change from the text shown
/// before, where the two start and stop differing, so LiveMarkdown builds again only the blocks from there on. Front
/// matter is blanked out first, keeping every line where it was. It keeps where the shown text's lines start, for
/// scrolling the preview with the editor.
/// </summary>
public sealed partial class MarkdownPreview : ObservableObject, IMarkdownUpdateProducer
{
    /// <summary>The Markdown grammar's scope: a file the editor colors with it is previewed.</summary>
    public const string Scope = "text.html.markdown";

    /// <summary>
    /// Past this many characters, about 1 MB, a file isn't previewed: rendering one holds the UI thread for seconds, since
    /// Avalonia makes and measures the controls there.
    /// </summary>
    public const int MaxLength = 1024 * 1024;

    private readonly List<IObserver<MarkdownDocumentUpdate>> _renderers = [];

    /// <summary>The last document handed on, and its text with the front matter blanked out.</summary>
    private MarkdownDocumentUpdate? _shown;
    private string? _shownText;

    /// <summary>The text last asked for, as typed: asked again, it's already on its way or shown.</summary>
    private string? _asked;
    private int _generation;
    private long _version;

    public ObservableStringBuilder? MarkdownBuilder
    {
        get => null;
        set => throw new NotSupportedException("A file's preview shows the editor's text.");
    }

    /// <summary>The text is over <see cref="MaxLength"/>: the preview says so rather than rendering it.</summary>
    [ObservableProperty]
    public partial bool IsTooLarge { get; private set; }

    /// <summary>Where each line of the text shown starts, as character offsets; the first is 0.</summary>
    public IReadOnlyList<int> LineStarts { get; private set; } = [0];

    /// <summary>The text shown, with its front matter blanked out; null before the first.</summary>
    internal string? ShownText => _shownText;

    /// <summary>The last change handed on, for tests: where the old and new texts differ.</summary>
    internal ObservableStringBuilderChangedEventArgs? LastChange { get; private set; }

    /// <summary>A new document was handed on: the view works out its places again once it has laid out.</summary>
    public event Action? Shown;

    /// <summary>The 1-based line of the text shown that holds the character at <paramref name="offset"/>.</summary>
    public int LineAt(int offset)
    {
        var starts = LineStarts;
        var low = 0;
        var high = starts.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (starts[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return low + 1;
    }

    /// <summary>
    /// Shows <paramref name="text"/>: parsed away from the UI thread, then handed to the renderers as a change from what
    /// they show. A newer call wins over one still parsing.
    /// </summary>
    public async Task ShowAsync(string text)
    {
        if (string.Equals(text, _asked, StringComparison.Ordinal))
        {
            return;
        }
        _asked = text;
        var generation = ++_generation;
        if (text.Length > MaxLength)
        {
            IsTooLarge = true;
            // What was shown goes, rather than holding a document nobody sees.
            Hand(null, null, [0], null);
            return;
        }
        var (masked, document, starts) = await Task.Run(() =>
        {
            var masked = WithoutFrontMatter(text);
            return (masked, Markdig.Markdown.Parse(masked, MarkdownUpdateProducer.DefaultPipeline), LineStartsOf(masked));
        });
        if (generation != _generation)
        {
            return;
        }
        IsTooLarge = false;
        var change = Difference(_shownText, masked, ++_version);
        MarkdownDocumentUpdate update = _shown is { } shown && _shownText is not null
            ? new MarkdownDocumentUpdate.Incremental(shown, document, change)
            : new MarkdownDocumentUpdate.Full(document, _version);
        Hand(update, masked, starts, change);
    }

    private void Hand(MarkdownDocumentUpdate? update, string? text, int[] starts, ObservableStringBuilderChangedEventArgs? change)
    {
        _shown = update;
        _shownText = text;
        LineStarts = starts;
        LastChange = change;
        foreach (var renderer in _renderers.ToArray())
        {
            // LiveMarkdown's renderer clears itself for none.
            renderer.OnNext(update!);
        }
        Shown?.Invoke();
    }

    public IDisposable Subscribe(IObserver<MarkdownDocumentUpdate> observer)
    {
        _renderers.Add(observer);
        if (_shown is { } shown)
        {
            observer.OnNext(shown);
        }
        return new Subscription(this, observer);
    }

    private sealed class Subscription(MarkdownPreview owner, IObserver<MarkdownDocumentUpdate> observer) : IDisposable
    {
        private MarkdownPreview? _owner = owner;

        public void Dispose()
        {
            _owner?._renderers.Remove(observer);
            _owner = null;
        }
    }

    /// <summary>
    /// Where <paramref name="before"/> and <paramref name="after"/> differ: from the first character that does, up to the
    /// characters both end with. With nothing before, all of it.
    /// </summary>
    internal static ObservableStringBuilderChangedEventArgs Difference(string? before, string after, long version)
    {
        if (before is null)
        {
            return new ObservableStringBuilderChangedEventArgs(0, 0, after.Length, version);
        }
        var start = before.AsSpan().CommonPrefixLength(after);
        var end = 0;
        var most = Math.Min(before.Length, after.Length) - start;
        while (end < most && before[before.Length - 1 - end] == after[after.Length - 1 - end])
        {
            end++;
        }
        return new ObservableStringBuilderChangedEventArgs(start, before.Length - start - end, after.Length - start - end, version);
    }

    /// <summary>
    /// <paramref name="text"/> with its front matter (a <c>---</c> line at the very top, YAML, and a <c>---</c> or
    /// <c>...</c> line closing it) turned to spaces, keeping its line breaks, so every line and character stays where it
    /// was and the preview shows nothing for it. Unclosed, it's left alone.
    /// </summary>
    internal static string WithoutFrontMatter(string text)
    {
        if (!IsFence(FirstLine(text, 0, out var next), "---"))
        {
            return text;
        }
        for (var at = next; at < text.Length;)
        {
            var line = FirstLine(text, at, out var after);
            if (IsFence(line, "---") || IsFence(line, "..."))
            {
                var blanked = text.ToCharArray(0, after);
                for (var i = 0; i < blanked.Length; i++)
                {
                    if (blanked[i] is not ('\r' or '\n'))
                    {
                        blanked[i] = ' ';
                    }
                }
                return string.Concat(new string(blanked), text.AsSpan(after));
            }
            at = after;
        }
        return text;
    }

    private static bool IsFence(ReadOnlySpan<char> line, string fence) => line.TrimEnd().SequenceEqual(fence);

    /// <summary>The line starting at <paramref name="start"/>, without its line break; <paramref name="next"/> is where the next starts.</summary>
    private static ReadOnlySpan<char> FirstLine(string text, int start, out int next)
    {
        var end = text.IndexOf('\n', start);
        next = end < 0 ? text.Length : end + 1;
        var line = text.AsSpan(start, (end < 0 ? text.Length : end) - start);
        return line.EndsWith("\r") ? line[..^1] : line;
    }

    private static int[] LineStartsOf(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
        {
            starts.Add(i + 1);
        }
        return [.. starts];
    }
}
