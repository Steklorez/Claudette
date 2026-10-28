using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using Claudette.App.ViewModels;
using Claudette.App.Views.Editor;

namespace Claudette.App.Views;

/// <summary>
/// Find in files' popup (DESIGN.md §18, "Finding in files"): the keyboard starts in its box; Up and Down choose a match,
/// Enter opens it, Ctrl/Cmd+Enter keeps the results in the Find window, Esc closes it. The selected match shows in its
/// file under the list, colored as the Files tab colors it, and picked out.
/// </summary>
public partial class FindInFilesView : UserControl
{
    private FindInFilesViewModel? _find;
    private TextMateColoring? _coloring;
    private string? _previewPath;

    public FindInFilesView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        });
        DetachedFromVisualTree += (_, _) =>
        {
            _coloring?.Dispose();
            _coloring = null;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_find is not null)
        {
            _find.PropertyChanged -= OnFindChanged;
        }
        _find = DataContext as FindInFilesViewModel;
        if (_find is not null)
        {
            _find.PropertyChanged += OnFindChanged;
            ShowPreview();
        }
    }

    private void OnFindChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindInFilesViewModel.Preview))
        {
            ShowPreview();
        }
    }

    /// <summary>The selected match's file in the preview, scrolled to it with the match selected.</summary>
    private void ShowPreview()
    {
        if (_find?.Preview is not { } preview)
        {
            return;
        }
        if (_previewPath != preview.Path || PreviewEditor.Document is null || PreviewEditor.Document.TextLength != preview.Text.Length)
        {
            _coloring?.Dispose();
            PreviewEditor.Document = new TextDocument(preview.Text);
            _coloring = TextMateColoring.Install(PreviewEditor.TextArea.TextView, PreviewEditor.Document, preview.Scope, ActualThemeVariant == ThemeVariant.Dark);
            _previewPath = preview.Path;
        }
        var document = PreviewEditor.Document;
        var line = document.GetLineByNumber(Math.Clamp(preview.Line, 1, document.LineCount));
        var start = line.Offset + Math.Clamp(preview.Column - 1, 0, line.Length);
        PreviewEditor.Select(start, Math.Clamp(preview.Length, 0, line.EndOffset - start));
        PreviewEditor.ScrollTo(line.LineNumber, preview.Column);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_find is not { } find)
        {
            return;
        }
        var primary = Services.Shortcuts.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == primary:
                find.KeepCommand.Execute(null);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                _ = find.OpenCommand.ExecuteAsync(null);
                break;
            case Key.Down:
                find.Move(1);
                ScrollToSelected();
                break;
            case Key.Up:
                find.Move(-1);
                ScrollToSelected();
                break;
            case Key.Escape:
                find.CloseCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void ScrollToSelected()
    {
        if (_find?.SelectedHit is { } hit)
        {
            HitList.ScrollIntoView(hit);
        }
    }

    private void OnHitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_find is { } find)
        {
            _ = find.OpenCommand.ExecuteAsync(null);
        }
    }
}
