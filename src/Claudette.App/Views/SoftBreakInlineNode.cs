using Avalonia.Controls.Documents;
using LiveMarkdown.Avalonia;
using Markdig.Syntax.Inlines;
using Inline = Avalonia.Controls.Documents.Inline;

namespace Claudette.App.Views;

/// <summary>
/// LiveMarkdown's line break node, with a soft break (a paragraph's line wrapped in the file) made a space in a Markdown
/// file's preview, as VS Code and GitHub show a file (DESIGN.md §18, "Markdown preview"). Everywhere else every break
/// stays a line break, as LiveMarkdown makes it: a line break in Claude's reply is meant. <see cref="App"/> puts it in
/// LiveMarkdown's pipeline in place of <see cref="LineBreakInlineNode"/>, for every Markdown view; a preview's renderer
/// has the <see cref="JoinsSoftBreaks"/> class.
/// </summary>
public sealed class SoftBreakInlineNode : InlineNode<LineBreakInline>
{
    /// <summary>The class of a renderer whose soft breaks are spaces.</summary>
    public const string JoinsSoftBreaks = "joinsoftbreaks";

    private Inline? _inline;

    public override Inline Inline => _inline ??= NewLineBreak();

    protected override bool UpdateCore(DocumentNode documentNode, LineBreakInline lineBreak, in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var space = !lineBreak.IsHard && documentNode.Owner.Classes.Contains(JoinsSoftBreaks);
        if (_inline is null)
        {
            _inline = space ? new Run(" ") : NewLineBreak();
            return true;
        }
        // Two spaces typed before it made it hard, or deleted, soft: it's made again.
        return space == (_inline is Run);
    }

    /// <summary>As LiveMarkdown's own node makes it.</summary>
    private static LineBreak NewLineBreak() => new() { Classes = { "LineBreak" } };
}
