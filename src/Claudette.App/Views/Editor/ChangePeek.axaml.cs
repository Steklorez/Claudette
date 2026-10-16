using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Claudette.App.ViewModels;
using Claudette.Core.ProjectFiles;

namespace Claudette.App.Views.Editor;

/// <summary>
/// The peek's content: its header and buttons, and the lines the change replaced in a read-only editor of their own, colored
/// as the file is.
/// </summary>
public partial class ChangePeek : UserControl
{
    private TextEditor? _old;
    private TextMateColoring? _coloring;

    public ChangePeek()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => _coloring?.UseDark(ActualThemeVariant == ThemeVariant.Dark);
    }

    /// <summary>Shows <paramref name="block"/>'s old lines, in the file's font and colors, one line high each.</summary>
    internal void Show(ChangeBlock block, ProjectFileViewModel file, FontFamily font, double fontSize, double lineHeight)
    {
        NothingRemoved.IsVisible = block.OldLines.Count == 0;
        OldLinesHost.IsVisible = block.OldLines.Count > 0;
        if (block.OldLines.Count == 0)
        {
            return;
        }
        if (_old is null)
        {
            _old = new TextEditor
            {
                IsReadOnly = true,
                ShowLineNumbers = false,
                Background = Brushes.Transparent,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(10, 2, 0, 2),
                MinWidth = 120,
            };
            _old.Options.EnableHyperlinks = false;
            _old.Options.EnableEmailHyperlinks = false;
            _old.Options.HighlightCurrentLine = false;
            // Read-only, and not where typing goes.
            _old.TextArea.Focusable = false;
            OldLinesHost.Child = _old;
        }
        _old.FontFamily = font;
        _old.FontSize = fontSize;
        _coloring?.Dispose();
        _old.Document = new TextDocument(string.Join("\n", block.OldLines));
        _coloring = TextMateColoring.Install(_old.TextArea.TextView, _old.Document, file.Language.Scope, ActualThemeVariant == ThemeVariant.Dark);
        // A scroll bar's room under the lines, for a line too long for the width.
        _old.Height = block.OldLines.Count * lineHeight + 6 + (block.OldLines.Any(l => l.Length > 200) ? 14 : 0);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _coloring?.Dispose();
        _coloring = null;
    }
}
