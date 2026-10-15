using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Claudette.App.ViewModels;
using Claudette.Core.Protocol;
using LiveMarkdown.Avalonia;

namespace Claudette.App.Views;

/// <summary>
/// Inline code in a reply's Markdown (DESIGN.md §18, "Files named in Claude's replies"), which <see cref="ReplyCodeInlineNode"/>
/// puts where LiveMarkdown would have put its own. When the code names a file (<c>`weapons.md`</c>,
/// <c>`docs/specs/weapons.md`</c>, <c>`Player.cpp:42`</c>) it asks its tab where that is, once it's in one, and becomes a
/// link when the file is found, so code that only looks like a name (<c>`System.IO.Path`</c>) stays as it is. A click
/// goes to the tab, as any link's does. Code inside a link stays code, so the link it's in still goes where it says.
/// </summary>
public sealed class ReplyFileCode : Span
{
    private readonly CodeInline _code = new();
    private ReplyFileReference? _reference;
    private Link? _link;
    private CancellationTokenSource? _finding;
    private MarkdownTextBlock? _waitingFor;

    public ReplyFileCode()
    {
        // As LiveMarkdown's own node makes it.
        _code.Classes.Add("Code");
        Inlines.Add(_code);
    }

    /// <summary>The code as the reply has it.</summary>
    public string Code => _code.Text ?? "";

    /// <summary>The file it names was found: it's a link.</summary>
    public bool IsLink => _link is not null;

    /// <summary>The reply streamed on and the code changed: it starts again from it.</summary>
    public void Show(string code)
    {
        if (_code.Text == code)
        {
            return;
        }
        _code.Text = code;
        _reference = ReplyFileReference.FromCode(code);
        Unlink();
        Find();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Find();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        StopFinding();
        base.OnDetachedFromLogicalTree(e);
    }

    private void Find()
    {
        StopFinding();
        if (_reference is not { } reference || _link is not null || !((ILogical)this).IsAttachedToLogicalTree
            || this.GetLogicalAncestors().OfType<Link>().Any())
        {
            return;
        }
        if (FindTab() is not { } tab)
        {
            // Not shown yet: its text block knows its tab once it is.
            if (this.FindLogicalAncestorOfType<MarkdownTextBlock>() is { } block && TopLevel.GetTopLevel(block) is null)
            {
                _waitingFor = block;
                block.AttachedToVisualTree += OnBlockShown;
            }
            return;
        }
        // A reply built again, as it scrolls back into view, links what was found at once.
        if (tab.ReplyFiles.Found(reference.Text) is { Count: > 0 })
        {
            Link(reference);
            return;
        }
        var finding = _finding = new CancellationTokenSource();
        _ = FindAsync(tab, reference, finding.Token);
    }

    private async Task FindAsync(TabViewModel tab, ReplyFileReference reference, CancellationToken cancellationToken)
    {
        try
        {
            var found = await tab.ReplyFiles.FindAsync(reference, clicked: false, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && found.Count > 0 && ReferenceEquals(reference, _reference))
            {
                Link(reference);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnBlockShown(object? sender, VisualTreeAttachmentEventArgs e) => Find();

    private void StopFinding()
    {
        _finding?.Cancel();
        _finding = null;
        if (_waitingFor is { } block)
        {
            block.AttachedToVisualTree -= OnBlockShown;
            _waitingFor = null;
        }
    }

    /// <summary>The tab it's in: the first ancestor whose data is one (the conversation, the side panel, an agent's window).</summary>
    private TabViewModel? FindTab() =>
        this.GetLogicalAncestors().OfType<StyledElement>().Select(a => a.DataContext).OfType<TabViewModel>().FirstOrDefault()
        ?? (this.FindLogicalAncestorOfType<MarkdownTextBlock>() is { } block
            ? block.GetVisualAncestors().OfType<StyledElement>().Select(a => a.DataContext).OfType<TabViewModel>().FirstOrDefault()
            : null);

    /// <summary>
    /// The code goes inside a link whose address is the code itself, escaped so no part of it reads as a scheme or a
    /// fragment; the tab's link command reads it back (<see cref="ReplyFileReference.FromLink"/>).
    /// </summary>
    private void Link(ReplyFileReference reference)
    {
        Inlines.Remove(_code);
        _link = new Link { HRef = new Uri(Uri.EscapeDataString(reference.Text), UriKind.Relative) };
        _link.Inlines.Add(_code);
        Inlines.Add(_link);
    }

    private void Unlink()
    {
        if (_link is null)
        {
            return;
        }
        _link.Inlines.Remove(_code);
        Inlines.Remove(_link);
        Inlines.Add(_code);
        _link = null;
    }
}
