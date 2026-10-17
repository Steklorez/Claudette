using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// A project run's log in the main area (DESIGN.md §18, "Project tools", "A run's log"). The run keeps its lines; this
/// mirrors them into the editor's document as they come and go, follows the newest line as a terminal does until the
/// log is scrolled up, and draws its links (<see cref="RunLogLinkGenerator"/>).
/// </summary>
public partial class ProjectRunView : UserControl
{
    private ProjectRunViewModel? _run;
    private BatchedCollection<string>? _output;

    /// <summary>How many of the run's lines the document has, one to a line.</summary>
    private int _mirrored;

    /// <summary>The log follows its newest line, until it's scrolled up from the end, and again once it's back there.</summary>
    private bool _following = true;

    /// <summary>A scroll to the end is on its way: changes until then go with it.</summary>
    private bool _scrollPending;

    public ProjectRunView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (_, _) => _run?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _run?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        // Its own links are drawn below; AvaloniaEdit's would open themselves, in a fixed blue.
        Log.Options.EnableHyperlinks = false;
        Log.Options.EnableEmailHyperlinks = false;
        Log.Options.HighlightCurrentLine = false;
        // #614: with it off, dragging a selection to the last line freezes the editor.
        Log.Options.AllowScrollBelowDocument = true;
        // Nothing is typed here, so there's no caret to show.
        Log.TextArea.Caret.CaretBrush = Brushes.Transparent;
        Log.Document = new TextDocument();
        var view = Log.TextArea.TextView;
        view.ElementGenerators.Add(new RunLogLinkGenerator(() => _run, () => Token("AccentStatusBrush"), link => _run?.OpenLinkCommand.Execute(link)));
        view.ScrollOffsetChanged += (_, _) =>
        {
            if (!_scrollPending && Log.IsEffectivelyVisible)
            {
                _following = IsAtEnd();
            }
        };
        ActualThemeVariantChanged += (_, _) => view.Redraw();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Unwatch();
        _run = DataContext as ProjectRunViewModel;
        if (this.IsAttachedToVisualTree())
        {
            Watch();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unwatch();
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch()
    {
        if (_run is null || _output is not null)
        {
            return;
        }
        _output = _run.Output;
        _output.CollectionChanged += OnOutputChanged;
        _run.LinksFound += OnLinksFound;
        _run.FindRequested += OnFindRequested;
        _run.PropertyChanged += OnRunPropertyChanged;
        Fill();
    }

    private void Unwatch()
    {
        if (_output is not null)
        {
            _output.CollectionChanged -= OnOutputChanged;
            _output = null;
        }
        if (_run is not null)
        {
            _run.LinksFound -= OnLinksFound;
            _run.FindRequested -= OnFindRequested;
            _run.PropertyChanged -= OnRunPropertyChanged;
        }
    }

    /// <summary>The whole log afresh, from its newest line.</summary>
    private void Fill()
    {
        Log.Document.Text = _output is null ? "" : string.Join("\n", _output.Select(OneLine));
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
        var document = Log.Document;
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
                if (Log.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
                {
                    scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, Log.TextArea.TextView.DocumentHeight - scroller.Viewport.Height));
                }
            }
            finally
            {
                _scrollPending = false;
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>A line of output as one line of the document, so the two keep step.</summary>
    private static string OneLine(string line) => line.Contains('\n') || line.Contains('\r') ? line.ReplaceLineEndings(" ") : line;

    /// <summary>Scrolled to the newest line, within a few pixels.</summary>
    private bool IsAtEnd() => Log.VerticalOffset + Log.ViewportHeight >= Log.TextArea.TextView.DocumentHeight - 4;

    /// <summary>Coming on screen: a log that was following picks up where it's got to.</summary>
    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectRunViewModel.IsShown) && _run is { IsShown: true })
        {
            FollowNewest();
        }
    }

    private void OnLinksFound() => Log.TextArea.TextView.Redraw();

    /// <summary><c>Ctrl/Cmd+F</c>: the editor's find, with a selection of one line in its box.</summary>
    private void OnFindRequested()
    {
        var search = Log.SearchPanel;
        if (Log.SelectedText is { Length: > 0 } selected && !selected.Contains('\n'))
        {
            search.SearchPattern = selected;
        }
        search.Open();
        // The first time, its box is only there once the panel has laid out; until then Reactivate does nothing.
        Avalonia.Threading.Dispatcher.UIThread.Post(search.Reactivate);
    }

    private IBrush? Token(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
