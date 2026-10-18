using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Claudette.App.Diffs;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>One open file in a Project Files tab: the chip fades while the cursor's line is under it.</summary>
public partial class ProjectFileView : UserControl
{
    public ProjectFileView()
    {
        InitializeComponent();
        CodeEditor.TextEditor.TextArea.Caret.PositionChanged += (_, _) => FadeChip();
        CodeEditor.TextEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => FadeChip();
    }

    /// <summary>The chip fades while the cursor's line is under it, so it never hides what's being typed.</summary>
    private void FadeChip()
    {
        var area = CodeEditor.TextEditor.TextArea;
        if (!Chip.IsVisible || area.TextView is not { VisualLinesValid: true } view)
        {
            return;
        }
        var caret = area.Caret.CalculateCaretRectangle();
        var top = caret.Y - view.ScrollOffset.Y;
        var origin = view.TranslatePoint(new Point(0, top), Chip);
        Chip.Classes.Set("faded", origin is { } at && at.Y + view.DefaultLineHeight > 0 && at.Y < Chip.Bounds.Height);
    }

    private void OnOpenInSystem(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectFileViewModel file && this.FindAncestorOfType<ProjectFilesView>()?.DataContext is ProjectFilesViewModel files)
        {
            files.OpenFileInSystemCommand.Execute(file);
        }
    }

    private void OnOpenInViewer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectFileViewModel file && this.FindAncestorOfType<ProjectFilesView>()?.DataContext is ProjectFilesViewModel files)
        {
            files.OpenFileInViewerCommand.Execute(file);
        }
    }

    /// <summary>A double-click on a line of the diff, not on its Revert: <b>Edit</b>, at that line.</summary>
    private void OnDiffDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectFileViewModel file && e.Source is StyledElement { DataContext: InlineDiffRow or SideBySideDiffRow } source
            && (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            file.EditAt(source.DataContext);
            e.Handled = true;
        }
    }
}
