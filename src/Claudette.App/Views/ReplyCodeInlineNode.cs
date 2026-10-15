using Avalonia.Controls.Documents;
using LiveMarkdown.Avalonia;
using MarkdigCodeInline = Markdig.Syntax.Inlines.CodeInline;

namespace Claudette.App.Views;

/// <summary>
/// LiveMarkdown's inline code node, with code that names a file made a link to it (DESIGN.md §18, "Files named in
/// Claude's replies"): the code is a <see cref="ReplyFileCode"/>, which looks for its file once it's in a tab.
/// <see cref="App"/> puts it in LiveMarkdown's pipeline in place of <see cref="CodeInlineNode"/>, for every Markdown view.
/// </summary>
public sealed class ReplyCodeInlineNode : InlineNode<MarkdigCodeInline>
{
    private readonly ReplyFileCode _code = new();

    public override Inline Inline => _code;

    protected override bool UpdateCore(DocumentNode documentNode, MarkdigCodeInline inline, in ObservableStringBuilderChangedEventArgs change, CancellationToken cancellationToken)
    {
        _code.Show(inline.Content);
        return true;
    }
}
