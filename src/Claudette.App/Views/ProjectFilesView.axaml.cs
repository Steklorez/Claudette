using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// A Project Files tab (DESIGN.md §18, "Project files"): the explorer placed on its side, its edge dragged to resize it,
/// the keyboard in the tree and the find box, and a click or the focus anywhere in it focusing its pane.
/// </summary>
public partial class ProjectFilesView : UserControl
{
    private ProjectFilesViewModel? _files;

    /// <summary>Where a drag of the explorer's edge started, and its width then; null when not resizing.</summary>
    private double? _resizeFrom;
    private double _resizeStartWidth;

    public ProjectFilesView()
    {
        InitializeComponent();
        // A click or the focus in the pane: the keyboard and the header follow this tab (DESIGN.md §18, "Split panes").
        AddHandler(PointerPressedEvent, (_, _) => _files?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _files?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        ExplorerEdge.PointerPressed += OnEdgePressed;
        ExplorerEdge.PointerMoved += OnEdgeMoved;
        ExplorerEdge.PointerReleased += (_, e) => EndResize(e.Pointer);
        ExplorerEdge.PointerCaptureLost += (_, _) => EndResize(null);
        ExplorerEdge.DoubleTapped += (_, _) => _files?.ResetExplorerWidth();
        // Dragging a file's tab along the row moves it there; its button keeps the pointer, so the row listens first.
        FileTabs.AddHandler(PointerPressedEvent, OnFileTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileTabs.AddHandler(PointerMovedEvent, OnFileTabsMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileTabs.AddHandler(PointerReleasedEvent, (_, _) => _tabDrag = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        ActualThemeVariantChanged += (_, _) => TellTheme();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_files is not null)
        {
            _files.PropertyChanged -= OnFilesChanged;
            _files.FindFocusRequested -= FocusFindBox;
        }
        _files = DataContext as ProjectFilesViewModel;
        if (_files is not null)
        {
            _files.PropertyChanged += OnFilesChanged;
            _files.FindFocusRequested += FocusFindBox;
            PlaceExplorer();
            TellTheme();
            if (_files.IsFinding)
            {
                FocusFindBox();
            }
        }
    }

    private void OnFilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectFilesViewModel.IsExplorerOnRight):
                PlaceExplorer();
                break;
            case nameof(ProjectFilesViewModel.Showing):
                Avalonia.Threading.Dispatcher.UIThread.Post(ShowShowingTab);
                break;
        }
    }

    /// <summary>The explorer on the left or the right, as Settings → Editor says, with its edge on the editor's side.</summary>
    private void PlaceExplorer()
    {
        var right = _files?.IsExplorerOnRight == true;
        Grid.SetColumn(Explorer, right ? 2 : 0);
        Grid.SetColumn(ExplorerEdge, right ? 2 : 0);
        ExplorerEdge.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ExplorerEdge.Margin = right ? new Thickness(-3, 0, 0, 0) : new Thickness(0, 0, -3, 0);
    }

    private void TellTheme()
    {
        if (_files is not null)
        {
            _files.IsDarkTheme = ActualThemeVariant == ThemeVariant.Dark;
        }
    }

    /// <summary>The tab of the file showing, scrolled into view in the row.</summary>
    private void ShowShowingTab()
    {
        if (_files?.Showing is { } showing && FileTabs.ContainerFromItem(showing) is Control tab)
        {
            tab.BringIntoView();
        }
    }

    private void FocusFindBox() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        FindBox.Focus();
        FindBox.SelectAll();
    });

    // ---- Resizing the explorer -----------------------------------------------------------------------------------

    private void OnEdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_files is null || !e.GetCurrentPoint(ExplorerEdge).Properties.IsLeftButtonPressed || e.ClickCount > 1)
        {
            return;
        }
        _resizeFrom = e.GetPosition(this).X;
        _resizeStartWidth = Explorer.Bounds.Width;
        e.Pointer.Capture(ExplorerEdge);
        e.Handled = true;
    }

    /// <summary>
    /// The explorer wider or narrower, leaving the editor its least (DESIGN.md §18, "Project files", "The layout"): on the
    /// right, dragging left widens it.
    /// </summary>
    private void OnEdgeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeFrom is not { } from || _files is null)
        {
            return;
        }
        var delta = e.GetPosition(this).X - from;
        var width = _resizeStartWidth + (_files.IsExplorerOnRight ? -delta : delta);
        _files.ResizeExplorer(Math.Min(width, Math.Max(ShellLayout.MinExplorerWidth, Bounds.Width - ShellLayout.MinConversationWidth)));
    }

    private void EndResize(IPointer? pointer)
    {
        if (_resizeFrom is null)
        {
            return;
        }
        _resizeFrom = null;
        pointer?.Capture(null);
        _files?.SaveExplorerWidth();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // A narrow pane: the explorer narrows first, down to its least, and the editor keeps its own.
        Explorer.MaxWidth = Math.Max(ShellLayout.MinExplorerWidth, e.NewSize.Width - ShellLayout.MinConversationWidth);
    }

    // ---- The keyboard and the pointer in the explorer ------------------------------------------------------------

    /// <summary>A click on a row: a file opens in the preview tab, a folder opens or closes.</summary>
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExplorerRow row)
        {
            _files?.OpenRowCommand.Execute(row);
        }
    }

    /// <summary>A double-click on a file: it opens to stay.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExplorerRow { IsFile: true } row)
        {
            _files?.OpenRowToStayCommand.Execute(row);
        }
    }

    /// <summary>Enter opens a file to stay; Right and Left open and close folders; Left on a file or a closed folder goes up.</summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_files is null || (sender as ListBox)?.SelectedItem is not ExplorerRow row || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                _files.OpenRowToStayCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Space when row.IsFile:
                _files.OpenRowCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Right when row.IsCollapsedFolder:
                _files.ToggleRowCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Left when row.IsExpanded:
                _files.ToggleRowCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Left when row.Depth > 0:
                var index = _files.Rows.IndexOf(row);
                _files.SelectedRow = _files.Rows.Take(index).LastOrDefault(r => r.Depth < row.Depth);
                e.Handled = true;
                break;
        }
    }

    /// <summary>In the find box: Up and Down choose, Enter opens, Esc brings the tree back.</summary>
    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (_files is null)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Down:
                _files.MoveFound(1);
                e.Handled = true;
                break;
            case Key.Up:
                _files.MoveFound(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _files.OpenFoundCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _files.StopFindCommand.Execute(null);
                TreeList.Focus();
                e.Handled = true;
                break;
        }
        if (e.Handled && _files.SelectedFound is { } found)
        {
            FoundList.ScrollIntoView(found);
        }
    }

    private void OnFoundTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FoundFile found)
        {
            _files?.OpenFoundCommand.Execute(found);
        }
    }

    // ---- The file tabs ------------------------------------------------------------------------------------------

    /// <summary>Double-clicking a preview's tab keeps it.</summary>
    private void OnFileTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ProjectFileViewModel file)
        {
            _files?.KeepFileCommand.Execute(file);
            e.Handled = true;
        }
    }

    /// <summary>The tab being dragged and where the press was; null when none is.</summary>
    private (ProjectFileViewModel File, Point From, bool Moving)? _tabDrag;

    private void OnFileTabsPressed(object? sender, PointerPressedEventArgs e)
    {
        _tabDrag = e.GetCurrentPoint(FileTabs).Properties.IsLeftButtonPressed && (e.Source as StyledElement)?.DataContext is ProjectFileViewModel file
            ? (file, e.GetPosition(FileTabs), false)
            : null;
    }

    /// <summary>Past a few pixels, the dragged tab takes the place of the tab under the pointer.</summary>
    private void OnFileTabsMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDrag is not { } drag || _files is null || !e.GetCurrentPoint(FileTabs).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var at = e.GetPosition(FileTabs);
        if (!drag.Moving && Math.Abs(at.X - drag.From.X) < 6)
        {
            return;
        }
        _tabDrag = drag with { Moving = true };
        for (var i = 0; i < _files.OpenFiles.Count; i++)
        {
            if (FileTabs.ContainerFromIndex(i) is { } tab && tab.TranslatePoint(default, FileTabs) is { } left && at.X >= left.X && at.X < left.X + tab.Bounds.Width)
            {
                _files.MoveFile(drag.File, i);
                return;
            }
        }
    }

    /// <summary>A middle click on a tab closes its file.</summary>
    private void OnFileTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && (sender as Control)?.DataContext is ProjectFileViewModel file)
        {
            _files?.CloseFileCommand.Execute(file);
            e.Handled = true;
        }
    }
}

