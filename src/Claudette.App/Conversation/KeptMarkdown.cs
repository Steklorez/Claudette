using Avalonia.Threading;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Conversation;

/// <summary>
/// A message's Markdown, parsed and kept for whichever renderer shows it (DESIGN.md §5). LiveMarkdown's renderer parses
/// its text in the background, so a renderer built for a reply started empty, one line tall, until the parse came back.
/// One showing this gets the parsed document as it subscribes: a reply built again shows at once, and one the
/// conversation builds out of view to learn its height has that height. While the text changes in view, as a reply
/// streams in, LiveMarkdown's own producer parses each change off the UI thread.
/// </summary>
public sealed class KeptMarkdown(ObservableStringBuilder text) : IMarkdownUpdateProducer
{
    /// <summary>What a renderer between messages shows: nothing new.</summary>
    public static IMarkdownUpdateProducer None { get; } = new Nothing();

    private readonly List<IObserver<MarkdownDocumentUpdate>> _renderers = [];

    /// <summary>The latest document: parsed here, or by <see cref="_live"/>.</summary>
    private MarkdownDocumentUpdate? _parsed;

    /// <summary>Parses the text's changes while it's shown; made at the first change.</summary>
    private MarkdownUpdateProducer? _live;
    private IDisposable? _following;

    public ObservableStringBuilder? MarkdownBuilder
    {
        get => text;
        set => throw new NotSupportedException("A message's Markdown stays its own.");
    }

    /// <summary>The renderers showing it now.</summary>
    internal int RendererCount => _renderers.Count;

    public IDisposable Subscribe(IObserver<MarkdownDocumentUpdate> observer)
    {
        Dispatcher.UIThread.VerifyAccess();
        _renderers.Add(observer);
        if (_renderers.Count == 1)
        {
            text.Changed += OnChanged;
        }
        if (_parsed is null || _parsed.Version != text.Version)
        {
            _parsed = new MarkdownDocumentUpdate.Full(Markdig.Markdown.Parse(text.ToString(), MarkdownUpdateProducer.DefaultPipeline), text.Version);
        }
        observer.OnNext(_parsed);
        return new Subscription(this, observer);
    }

    /// <summary>The text changed while shown: from now on LiveMarkdown's producer parses its changes, in the background.</summary>
    private void OnChanged(in ObservableStringBuilderChangedEventArgs change)
    {
        text.Changed -= OnChanged;
        _live ??= new MarkdownUpdateProducer { MarkdownBuilder = text };
        _following = _live.Subscribe(new Relay(this));
    }

    private void Publish(MarkdownDocumentUpdate update)
    {
        _parsed = update;
        foreach (var renderer in _renderers.ToArray())
        {
            renderer.OnNext(update);
        }
    }

    private void Unsubscribe(IObserver<MarkdownDocumentUpdate> observer)
    {
        Dispatcher.UIThread.VerifyAccess();
        _renderers.Remove(observer);
        if (_renderers.Count == 0)
        {
            text.Changed -= OnChanged;
            _following?.Dispose();
            _following = null;
        }
    }

    private sealed class Subscription(KeptMarkdown owner, IObserver<MarkdownDocumentUpdate> observer) : IDisposable
    {
        private KeptMarkdown? _owner = owner;

        public void Dispose()
        {
            _owner?.Unsubscribe(observer);
            _owner = null;
        }
    }

    /// <summary>Passes on what LiveMarkdown's producer parsed.</summary>
    private sealed class Relay(KeptMarkdown owner) : IObserver<MarkdownDocumentUpdate>
    {
        public void OnNext(MarkdownDocumentUpdate value) => owner.Publish(value);

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class Nothing : IMarkdownUpdateProducer, IDisposable
    {
        public ObservableStringBuilder? MarkdownBuilder { get => null; set { } }

        public IDisposable Subscribe(IObserver<MarkdownDocumentUpdate> observer) => this;

        public void Dispose()
        {
        }
    }
}
