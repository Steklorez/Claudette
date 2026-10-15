using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Claudette.App.Controls;
using Claudette.App.Conversation;
using Claudette.App.Diffs;
using Claudette.App.Services;
using Claudette.App.ViewModels;
using Claudette.Core.Settings;

namespace Claudette.App.Views;

/// <summary>
/// One tab: the conversation, find, the prompts over it and the side panel's width. The side panel's pages are
/// <see cref="SidePanelView"/>, the composer <see cref="ComposerView"/>, and their styles <c>Themes/ConversationStyles.axaml</c>.
/// </summary>
public partial class TabView : UserControl
{
    private bool _stickToBottom = true;

    /// <summary>
    /// The reader is scrolling up (the wheel, a scroll bar, a key, a swipe): the next move up is theirs, even when the
    /// content grows in the same layout pass. Without it, a move up is the list correcting its estimate of the
    /// messages above, which keeps its place at the bottom.
    /// </summary>
    private bool _readerScrolling;

    /// <summary>Where a drag of the side panel's edge started, and the width then; null when not resizing.</summary>
    private double? _resizeFrom;
    private double _resizeStartWidth;

    public TabView()
    {
        InitializeComponent();
        // Tunnel, so the prompt's shortcuts are seen before the composer turns Enter into a new line.
        AddHandler(KeyDownEvent, OnPromptKeyDown, RoutingStrategies.Tunnel);
        // With tabs side by side, a click or the focus in a pane makes it the focused one (DESIGN.md §18, "Split panes").
        AddHandler(PointerPressedEvent, (_, _) => ViewModel?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => ViewModel?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        ComposerPanel.Sent += () => _stickToBottom = true;
        // Claudette on the composer keeps clear of Jump to latest, and ducks while a thread's strip or a dialog sits over
        // the composer (DESIGN.md §5).
        ComposerPanel.KeepMascotClearOf([JumpToLatest, ThreadStrip, .. ConversationArea.Children.OfType<Border>().Where(b => b.Classes.Contains("overlay"))]);
        ConversationScroll.ScrollChanged += OnConversationScrollChanged;
        // Seen even when the scroll viewer handles them.
        ConversationScroll.AddHandler(PointerWheelChangedEvent, (_, e) => _readerScrolling |= e.Delta.Y > 0, RoutingStrategies.Tunnel, handledEventsToo: true);
        ConversationScroll.AddHandler(PointerPressedEvent, (_, e) => _readerScrolling |= IsOnScrollBar(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        ConversationScroll.AddHandler(PointerMovedEvent, (_, e) => _readerScrolling |= IsOnScrollBar(e.Source) && e.GetCurrentPoint(ConversationScroll).Properties.IsLeftButtonPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ConversationScroll.AddHandler(KeyDownEvent, (_, e) => _readerScrolling |= e.Key is Key.Up or Key.PageUp or Key.Home, RoutingStrategies.Tunnel, handledEventsToo: true);
        ConversationScroll.AddHandler(InputElement.ScrollGestureEvent, (_, e) => _readerScrolling |= e.Delta.Y < 0, RoutingStrategies.Tunnel, handledEventsToo: true);
        ConversationItems.ContainerPrepared += OnConversationContainerPrepared;
        ScrollMarks.MarkClicked += mark => ScrollItemToTop(mark.Index);
        ActualThemeVariantChanged += (_, _) => UpdateMarks();
        // Copy on a code block goes through the tab and says "Copied" (DESIGN.md §5, "Copy and times").
        CodeBlockCopy.Attach(this);
        // Add to scratch pad: a code block's button, and the menu of text selected in the conversation (DESIGN.md §18).
        ScratchPadAdd.Attach(this, ConversationScroll);
        SidePanelEdge.PointerPressed += OnSidePanelEdgePressed;
        SidePanelEdge.PointerMoved += OnSidePanelEdgeMoved;
        SidePanelEdge.PointerReleased += (_, e) => EndSidePanelResize(e.Pointer);
        SidePanelEdge.PointerCaptureLost += (_, _) => EndSidePanelResize(null);
        SidePanelEdge.DoubleTapped += (_, _) => ViewModel?.ResetSidePanelWidth();
        // However wide it was dragged, the side panel leaves the conversation its least (DESIGN.md §3, "Side panel").
        SizeChanged += (_, e) => SidePanel.MaxWidth = Math.Max(ShellLayout.MinSidePanelWidth, e.NewSize.Width - ShellLayout.MinConversationWidth);
    }

    private TabViewModel? ViewModel => DataContext as TabViewModel;

    // ---- Resizing the side panel (DESIGN.md §3) -------------------------------------------------------------------

    private void OnSidePanelEdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || !e.GetCurrentPoint(SidePanelEdge).Properties.IsLeftButtonPressed || e.ClickCount > 1)
        {
            return;
        }
        // Measured against the whole view, which doesn't move as the edge does, from the width it shows.
        _resizeFrom = e.GetPosition(this).X;
        _resizeStartWidth = SidePanel.Bounds.Width;
        e.Pointer.Capture(SidePanelEdge);
        e.Handled = true;
    }

    /// <summary>The panel is on the right, so dragging its edge left widens it.</summary>
    private void OnSidePanelEdgeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeFrom is { } from)
        {
            ViewModel?.ResizeSidePanel(Math.Min(_resizeStartWidth + from - e.GetPosition(this).X, SidePanel.MaxWidth));
        }
    }

    private void EndSidePanelResize(IPointer? pointer)
    {
        if (_resizeFrom is null)
        {
            return;
        }
        _resizeFrom = null;
        pointer?.Capture(null);
        ViewModel?.SaveSidePanelWidth();
    }

    /// <summary>The Perforce password prompt opened: type straight into it.</summary>
    private void OnPerforcePasswordAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus());
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel is not { } tab)
        {
            return;
        }
        if (Shortcuts.Matches(tab.Keyboard, KeyboardShortcuts.Stop, e.Key, e.KeyModifiers) && tab.StopCommand.CanExecute(null))
        {
            tab.StopCommand.Execute(null);
            e.Handled = true;
        }
        else if (Shortcuts.Matches(tab.Keyboard, KeyboardShortcuts.Suffixes, e.Key, e.KeyModifiers))
        {
            ComposerPanel.ShowSuffixes();
            e.Handled = true;
        }
        else if (Shortcuts.Matches(tab.Keyboard, KeyboardShortcuts.Stash, e.Key, e.KeyModifiers))
        {
            // DESIGN.md §5, "Drafts and the stash".
            tab.StashCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl/Cmd+Enter answers "yes" to the waiting prompt, or sends an MCP server's form, and Ctrl/Cmd+Backspace "no"
    /// (DESIGN.md §7), or whatever Settings → Keyboard says. Not while typing in one of a prompt's own fields, and
    /// Backspace keeps deleting words in a field with text.
    /// </summary>
    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { HasKeyboardAnswer: true } tab)
        {
            return;
        }
        var allow = Shortcuts.Matches(tab.Keyboard, KeyboardShortcuts.AllowPrompt, e.Key, e.KeyModifiers);
        if (!allow && !Shortcuts.Matches(tab.Keyboard, KeyboardShortcuts.DenyPrompt, e.Key, e.KeyModifiers))
        {
            return;
        }
        var focusedBox = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
        if (focusedBox is not null && !ComposerPanel.HasFocus(focusedBox)
            || e.Key == Key.Back && !string.IsNullOrEmpty(focusedBox?.Text))
        {
            return;
        }
        // With Ctrl/Cmd+Enter as the send key (DESIGN.md §14), it sends what's typed in the composer; with nothing typed it
        // answers the prompt.
        if (allow && e.Key == Key.Enter && tab.Keyboard.SendKey == SendKey.PrimaryEnter && ComposerPanel.HasFocus(focusedBox) && tab.SendCommand.CanExecute(null))
        {
            return;
        }
        e.Handled = allow ? tab.AcceptWaitingPrompt() : tab.DeclineWaitingPrompt();
    }

    /// <summary>Follows new output unless the user has scrolled up; then offers "Jump to latest" (DESIGN.md §5).</summary>
    private void OnConversationScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scroll = ConversationScroll;
        var distanceFromBottom = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y;
        (_stickToBottom, var scrollToEnd) = FollowOutput.Update(_stickToBottom, _readerScrolling, e.OffsetDelta.Y, e.ExtentDelta.Y, distanceFromBottom);
        _readerScrolling = false;
        if (scrollToEnd)
        {
            ScrollToEnd();
        }
        JumpToLatest.IsVisible = !_stickToBottom && distanceFromBottom > FollowOutput.Threshold;
    }

    private static bool IsOnScrollBar(object? source) => source is Visual visual && visual.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null;

    private void OnJumpToLatest(object? sender, RoutedEventArgs e)
    {
        _stickToBottom = true;
        ScrollToEnd();
        JumpToLatest.IsVisible = false;
    }

    private void ScrollToEnd()
    {
        var scroll = ConversationScroll;
        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_tab is not null)
        {
            _tab.ChangedFiles.DiffRequested -= OnDiffRequested;
            _tab.ImageRequested -= OnImageRequested;
            _tab.ToolWindowRequested -= OnToolWindowRequested;
            _tab.ScrollToRequested -= OnScrollToRequested;
            _tab.MessageJumpRequested -= OnMessageJumpRequested;
            _tab.AgentWindowRequested -= OnAgentWindowRequested;
            _tab.FindFocusRequested -= OnFindFocusRequested;
            _tab.Find.PropertyChanged -= OnFindPropertyChanged;
        }
        _tab = ViewModel;
        if (_tab is not null)
        {
            _tab.ChangedFiles.DiffRequested += OnDiffRequested;
            _tab.ImageRequested += OnImageRequested;
            _tab.ToolWindowRequested += OnToolWindowRequested;
            _tab.ScrollToRequested += OnScrollToRequested;
            _tab.MessageJumpRequested += OnMessageJumpRequested;
            _tab.AgentWindowRequested += OnAgentWindowRequested;
            _tab.FindFocusRequested += OnFindFocusRequested;
            _tab.Find.PropertyChanged += OnFindPropertyChanged;
        }
        MarkFindCurrent();
    }

    private TabViewModel? _tab;

    // ---- Find in the conversation (DESIGN.md §5, "Find") ---------------------------------------------------------

    /// <summary>The item marked as the current match, whose container has the <c>findcurrent</c> class.</summary>
    private ConversationItem? _findMarked;

    private void OnFindFocusRequested() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        FindBox.Focus();
        FindBox.SelectAll();
    });

    private void OnFindPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationSearch.Current))
        {
            MarkFindCurrent();
        }
    }

    /// <summary>
    /// Marks the current match's container, or the Tool calls card's when the match is one of its calls (whose tile is
    /// open); the conversation is virtualized, so a new container is marked as it's made.
    /// </summary>
    private void MarkFindCurrent()
    {
        if (_findMarked is not null && ConversationItems.ContainerFromItem(_findMarked) is { } old)
        {
            old.Classes.Remove("findcurrent");
        }
        _findMarked = _tab?.Find.Current is { } match ? _tab.TopLevelItemOf(match) ?? match : null;
        if (_findMarked is not null && ConversationItems.ContainerFromItem(_findMarked) is { } current)
        {
            current.Classes.Add("findcurrent");
        }
    }

    private void OnConversationContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        e.Container.Classes.Set("findcurrent", _findMarked is not null && ReferenceEquals(ConversationItems.ItemFromContainer(e.Container), _findMarked));
        if (ConversationItems.ItemsPanelRoot is ConversationPanel panel && !ReferenceEquals(panel, _panel))
        {
            if (_panel is not null)
            {
                _panel.Arranged -= OnPanelArranged;
            }
            _panel = panel;
            _panel.Arranged += OnPanelArranged;
        }
    }

    // ---- Marks beside the scroll bar, and going from message to message (DESIGN.md §18, "Conversation cards") ----

    /// <summary>The conversation's panel, once it has items: where each one is.</summary>
    private ConversationPanel? _panel;

    private void OnPanelArranged(object? sender, EventArgs e) => UpdateMarks();

    /// <summary>Where the panel starts in the scroll viewer's content: below the list's margin.</summary>
    private double PanelOrigin(ConversationPanel panel) =>
        ConversationItems.Margin.Top + (panel.TranslatePoint(default, ConversationItems)?.Y ?? 0);

    /// <summary>The marks for the items worth finding again, where the panel last laid them out; none while all of it fits.</summary>
    private void UpdateMarks()
    {
        if (_panel is not { } panel || _tab is not { } tab)
        {
            return;
        }
        var scroll = ConversationScroll;
        var extent = scroll.Extent.Height;
        List<ScrollMark> marks = [];
        if (extent > scroll.Viewport.Height + 1)
        {
            var origin = PanelOrigin(panel);
            var brushes = new Dictionary<LandmarkKind, IBrush>();
            for (var i = 0; i < tab.Items.Count; i++)
            {
                if (ConversationLandmarks.KindOf(tab.Items[i]) is not { } kind)
                {
                    continue;
                }
                if (!brushes.TryGetValue(kind, out var brush))
                {
                    brush = brushes[kind] = MarkBrush(kind);
                }
                marks.Add(new ScrollMark((origin + panel.TopOf(i)) / extent, brush, i, ConversationLandmarks.Describe(tab.Items[i])));
            }
        }
        if (!marks.SequenceEqual(ScrollMarks.Marks))
        {
            ScrollMarks.Marks = marks;
        }
    }

    private IBrush MarkBrush(LandmarkKind kind) =>
        this.FindResource(ActualThemeVariant, kind switch
        {
            LandmarkKind.Prompt => "CardYouStripeBrush",
            LandmarkKind.Waiting => "MeterWarningBrush",
            LandmarkKind.Error => "ErrorTextBrush",
            _ => "CardFilesStripeBrush",
        }) as IBrush ?? Brushes.Gray;

    /// <summary>F3, F4 and their Ctrl/Cmd forms: the message before or after the one at the top of the view, the first, the latest.</summary>
    private void OnMessageJumpRequested(MessageJump jump)
    {
        if (jump == MessageJump.Latest)
        {
            OnJumpToLatest(this, new RoutedEventArgs());
            return;
        }
        if (_panel is not { } panel || _tab is not { } tab)
        {
            return;
        }
        var items = tab.Items;
        var target = ConversationLandmarks.First(items);
        if (jump != MessageJump.First)
        {
            var viewTop = ConversationScroll.Offset.Y - PanelOrigin(panel);
            var at = panel.ItemAt(Math.Max(0, viewTop + 1));
            // A message the top of the view cuts off is the one before: going back starts with its own top.
            target = jump == MessageJump.Previous && at >= 0 && at < items.Count && panel.TopOf(at) < viewTop - 1 && ConversationLandmarks.IsMessage(items[at])
                ? at
                : ConversationLandmarks.Step(items, at, forward: jump == MessageJump.Next);
        }
        if (target >= 0)
        {
            ScrollItemToTop(target);
        }
    }

    /// <summary>
    /// Puts an item's top at the top of the view. The panel may only have estimated where it is, so once it's laid out
    /// the view lines it up again.
    /// </summary>
    private void ScrollItemToTop(int index)
    {
        if (_panel is not { } panel)
        {
            return;
        }
        void Align(double top)
        {
            // The reader moved, so the conversation stops following new output (DESIGN.md §5).
            _stickToBottom = false;
            _readerScrolling = true;
            var max = Math.Max(0, ConversationScroll.Extent.Height - ConversationScroll.Viewport.Height);
            ConversationScroll.Offset = new Vector(ConversationScroll.Offset.X, Math.Clamp(top - 8, 0, max));
        }
        Align(PanelOrigin(panel) + panel.TopOf(index));
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ConversationItems.ContainerFromIndex(index) is { } container && container.TranslatePoint(default, ConversationItems) is { } at)
            {
                Align(ConversationItems.Margin.Top + at.Y);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Enter goes to the next match, Shift+Enter the one before, Esc closes the bar.</summary>
    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } tab)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.Shift:
                tab.Find.PreviousCommand.Execute(null);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                tab.Find.NextCommand.Execute(null);
                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                CloseFind(tab);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnCloseFind(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tab)
        {
            CloseFind(tab);
        }
    }

    /// <summary>Closes the bar, and the focus goes back to the composer.</summary>
    private void CloseFind(TabViewModel tab)
    {
        tab.Find.CloseCommand.Execute(null);
        ComposerPanel.FocusComposer();
    }

    /// <summary>The built-in diff view, in its own window so it can stay open beside the conversation (DESIGN.md §8).</summary>
    private void OnDiffRequested(DiffSource source)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var window = new DiffWindow { DataContext = new DiffWindowViewModel(source, dark) };
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>An image from a tool's result, full size in a window of its own (DESIGN.md §18, "Images from Claude's tools").</summary>
    private void OnImageRequested(ImageViewerViewModel viewer)
    {
        var window = new ImageWindow { DataContext = viewer };
        window.Closed += (_, _) => viewer.Dispose();
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.ShowBeside(owner);
        }
        else
        {
            window.Show();
        }
    }

    // The tool calls open in windows of their own: opening one again brings its window to the front.
    private readonly Dictionary<ToolUseItem, ToolCallWindow> _toolWindows = [];

    /// <summary>A tool call in a window of its own, which follows it while it runs (DESIGN.md §18, "Conversation cards").</summary>
    private void OnToolWindowRequested(ToolUseItem tool)
    {
        if (_toolWindows.TryGetValue(tool, out var open))
        {
            open.Activate();
            return;
        }
        if (_tab is not { } tab)
        {
            return;
        }
        var window = new ToolCallWindow { DataContext = tab.ToolWindowFor(tool, ActualThemeVariant == ThemeVariant.Dark) };
        _toolWindows[tool] = window;
        window.Closed += (_, _) => _toolWindows.Remove(tool);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// Scrolls to a card, for example the Bash call that started a process, or a subagent's group from the agent map.
    /// A group nested in another only has a container once the groups around it have expanded and been laid out.
    /// </summary>
    private void OnScrollToRequested(ConversationItem item)
    {
        _stickToBottom = false;
        if (item is ToolUseItem tool)
        {
            tool.IsExpanded = true;
        }
        if (ConversationItems.ContainerFromItem(item) is { } container)
        {
            BringTopIntoView(container);
            return;
        }
        // The conversation is virtualized: bring the top-level item that holds it into view first, so it has controls.
        if (ViewModel?.TopLevelItemOf(item) is { } outer)
        {
            ConversationItems.ScrollIntoView(outer);
            if (ReferenceEquals(outer, item) && ConversationItems.ContainerFromItem(item) is { } realized)
            {
                BringTopIntoView(realized);
                return;
            }
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ConversationItems.ContainerFromItem(item) is { } top)
            {
                BringTopIntoView(top);
            }
            else if (ConversationItems.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => ReferenceEquals(p.Content, item)) is { } nested)
            {
                BringTopIntoView(nested);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The top of a card, so a long expanded group starts at its header.</summary>
    private static void BringTopIntoView(Control container) =>
        container.BringIntoView(new Rect(0, 0, container.Bounds.Width, Math.Min(container.Bounds.Height, 80)));

    private AgentMapWindow? _agentWindow;

    /// <summary>The agent map in a window of its own, beside the conversation (DESIGN.md §18); one per tab.</summary>
    private void OnAgentWindowRequested()
    {
        if (_agentWindow is not null)
        {
            _agentWindow.Activate();
            return;
        }
        var window = _agentWindow = new AgentMapWindow { DataContext = DataContext };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_agentWindow, window))
            {
                _agentWindow = null;
            }
        };
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>The tab closed: its agent map window goes with it.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _agentWindow?.Close();
    }
}
