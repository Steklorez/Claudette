using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Claudette.App.ViewModels;
using Claudette.Core.ProjectTools.EditorLogs;

namespace Claudette.App.Views;

/// <summary>
/// Keeps a read-only editor showing a log in the main area (DESIGN.md §18, "Project tools", "A run's log"; "Editor
/// logs"). The log keeps its lines; this mirrors them into the editor's document as they come and go, follows the newest
/// line as a terminal does until the log is scrolled up, draws its links (<see cref="RunLogLinkGenerator"/>), colors an
/// editor's error and warning lines (<see cref="LogLineColorizer"/>) and opens find. <see cref="ProjectRunView"/> and
/// <see cref="EditorLogView"/> each have one.
/// </summary>
internal sealed class LogMirror
{
    private readonly Control _view;
    private readonly TextEditor _editor;
    private LogTabViewModel? _log;
    private BatchedCollection<string>? _output;

    /// <summary>How many of the log's lines the document has, one to a line.</summary>
    private int _mirrored;

    /// <summary>The log follows its newest line, until it's scrolled up from the end, and again once it's back there.</summary>
    private bool _following = true;

    /// <summary>A scroll to the end is on its way: changes until then go with it.</summary>
    private bool _scrollPending;

    public LogMirror(Control view, TextEditor editor)
    {
        _view = view;
        _editor = editor;
        view.AddHandler(InputElement.PointerPressedEvent, (_, _) => _log?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AddHandler(InputElement.GotFocusEvent, (_, _) => _log?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        // Its own links are drawn below; AvaloniaEdit's would open themselves, in a fixed blue.
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.HighlightCurrentLine = false;
        // #614: with it off, dragging a selection to the last line freezes the editor.
        editor.Options.AllowScrollBelowDocument = true;
        // Nothing is typed here, so there's no caret to show.
        editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        editor.Document = new TextDocument();
        var textView = editor.TextArea.TextView;
        textView.LineTransformers.Add(new LogLineColorizer(() => _log as EditorLogViewModel, Token));
        textView.ElementGenerators.Add(new RunLogLinkGenerator(() => _log, () => Token("AccentStatusBrush"), link => _log?.OpenLinkCommand.Execute(link)));
        textView.ScrollOffsetChanged += (_, _) =>
        {
            if (!_scrollPending && editor.IsEffectivelyVisible)
            {
                _following = IsAtEnd();
            }
        };
        view.ActualThemeVariantChanged += (_, _) => textView.Redraw();
    }

    /// <summary>The view's data context changed: it shows <paramref name="log"/> from now on.</summary>
    public void Use(LogTabViewModel? log)
    {
        Unwatch();
        _log = log;
        if (_view.IsAttachedToVisualTree())
        {
            Watch();
        }
    }

    /// <summary>The view is on the visual tree: the log's lines follow it from now on.</summary>
    public void Watch()
    {
        if (_log is null || _output is not null)
        {
            return;
        }
        _output = _log.Output;
        _output.CollectionChanged += OnOutputChanged;
        _log.LinksFound += OnLinksFound;
        _log.FindRequested += OnFindRequested;
        _log.PropertyChanged += OnLogPropertyChanged;
        Fill();
    }

    public void Unwatch()
    {
        if (_output is not null)
        {
            _output.CollectionChanged -= OnOutputChanged;
            _output = null;
        }
        if (_log is not null)
        {
            _log.LinksFound -= OnLinksFound;
            _log.FindRequested -= OnFindRequested;
            _log.PropertyChanged -= OnLogPropertyChanged;
        }
    }

    /// <summary>The whole log afresh, from its newest line.</summary>
    private void Fill()
    {
        _editor.Document.Text = _output is null ? "" : string.Join("\n", _output.Select(OneLine));
        _mirrored = _output?.Count ?? 0;
        _following = true;
        FollowNewest();
    }

    /// <summary>
    /// A batch of lines is one Add, after a Remove when the oldest were dropped, or a Reset when it replaced them all
    /// (<see cref="BatchedCollection{T}.AddAndTrim"/>).
    /// </summary>
    private void OnOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var document = _editor.Document;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { Count: > 0 } added && e.NewStartingIndex == _mirrored:
                document.Insert(document.TextLength, (_mirrored == 0 ? "" : "\n") + string.Join("\n", added.Cast<string>().Select(OneLine)));
                _mirrored += added.Count;
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex == 0 && e.OldItems is { Count: > 0 } removed && removed.Count < _mirrored:
                document.Remove(0, document.GetLineByNumber(removed.Count + 1).Offset);
                _mirrored -= removed.Count;
                return;
            default:
                Fill();
                return;
        }
        FollowNewest();
    }

    private void FollowNewest()
    {
        if (!_following || _scrollPending)
        {
            return;
        }
        _scrollPending = true;
        // Once the new lines are laid out.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // The editor's own ScrollToVerticalOffset doesn't move it. The newest line goes at the bottom, not the
                // top, where scrolling to the end would put it with room below the document.
                if (_editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
                {
                    scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, _editor.TextArea.TextView.DocumentHeight - scroller.Viewport.Height));
                }
            }
            finally
            {
                _scrollPending = false;
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>A line of the log as one line of the document, so the two keep step.</summary>
    private static string OneLine(string line) => line.Contains('\n') || line.Contains('\r') ? line.ReplaceLineEndings(" ") : line;

    /// <summary>Scrolled to the newest line, within a few pixels.</summary>
    private bool IsAtEnd() => _editor.VerticalOffset + _editor.ViewportHeight >= _editor.TextArea.TextView.DocumentHeight - 4;

    /// <summary>Coming on screen: a log that was following picks up where it's got to.</summary>
    private void OnLogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogTabViewModel.IsShown) && _log is { IsShown: true })
        {
            FollowNewest();
        }
    }

    private void OnLinksFound() => _editor.TextArea.TextView.Redraw();

    /// <summary><c>Ctrl/Cmd+F</c>: the editor's find, with a selection of one line in its box.</summary>
    private void OnFindRequested()
    {
        var search = _editor.SearchPanel;
        if (_editor.SelectedText is { Length: > 0 } selected && !selected.Contains('\n'))
        {
            search.SearchPattern = selected;
        }
        search.Open();
        // The first time, its box is only there once the panel has laid out; until then Reactivate does nothing.
        Avalonia.Threading.Dispatcher.UIThread.Post(search.Reactivate);
    }

    private IBrush? Token(string key) => _view.TryFindResource(key, _view.ActualThemeVariant, out var value) ? value as IBrush : null;
}

/// <summary>
/// Colors an editor's log (DESIGN.md §18, "Editor logs"): its error lines in the error color and its warnings in the
/// warning color, by the engine's own markers (<see cref="EditorLogViewModel.LevelOf"/>). A run's log isn't colored.
/// </summary>
internal sealed class LogLineColorizer(Func<EditorLogViewModel?> log, Func<string, IBrush?> token) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (log() is not { } editorLog || line.Length == 0)
        {
            return;
        }
        var level = editorLog.LevelOf(CurrentContext.Document.GetText(line));
        if (level == LogLineLevel.Normal || token(level == LogLineLevel.Error ? "ErrorTextBrush" : "WarningTextBrush") is not { } brush)
        {
            return;
        }
        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
    }
}
