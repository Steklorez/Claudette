using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using Claudette.App.ViewModels;
using Claudette.Core.ProjectTools;

namespace Claudette.App.Views;

/// <summary>
/// Draws the links in a run's log (DESIGN.md §18, "Project tools", "A run's log"): each web address, and each path once
/// it's found, in the accent, underlined, with the hand pointer. A click opens it; it doesn't start a selection.
/// </summary>
/// <param name="run">The run whose log is showing, which says which parts of a line are links.</param>
/// <param name="brush">The links' color, from the theme.</param>
/// <param name="clicked">What a click on one does.</param>
internal sealed class RunLogLinkGenerator(Func<ProjectRunViewModel?> run, Func<IBrush?> brush, Action<RunLogLink> clicked) : VisualLineElementGenerator
{
    private static Cursor Hand => field ??= new(StandardCursorType.Hand);

    /// <summary>The links of the line being built, so a line is looked through once however many links it has.</summary>
    private IReadOnlyList<RunLogLink> _links = [];

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _links = run()?.LinksIn(context.Document.GetText(context.VisualLine.FirstDocumentLine)) ?? [];
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var lineStart = CurrentContext.VisualLine.FirstDocumentLine.Offset;
        foreach (var link in _links)
        {
            if (lineStart + link.Start >= startOffset)
            {
                return lineStart + link.Start;
            }
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var start = offset - CurrentContext.VisualLine.FirstDocumentLine.Offset;
        return _links.FirstOrDefault(l => l.Start == start) is { } link
            ? new LinkText(CurrentContext.VisualLine, link.Length, link, brush(), clicked)
            : null;
    }

    /// <summary>One link's text.</summary>
    private sealed class LinkText(VisualLine line, int length, RunLogLink link, IBrush? brush, Action<RunLogLink> clicked) : VisualLineText(line, length)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            if (brush is not null)
            {
                TextRunProperties.SetForegroundBrush(brush);
            }
            TextRunProperties.SetTextDecorations(TextDecorations.Underline);
            return base.CreateTextRun(startVisualColumn, context);
        }

        // A link that wraps onto the next row is split in two, each still the link.
        protected override VisualLineText CreateInstance(int length) => new LinkText(ParentVisualLine, length, link, brush, clicked);

        protected override void OnQueryCursor(PointerEventArgs e)
        {
            if (e.Source is InputElement element)
            {
                element.Cursor = Hand;
            }
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.Handled || e.ClickCount != 1 || e.KeyModifiers is not KeyModifiers.None || e.Source is not InputElement element
                || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            clicked(link);
        }
    }
}
