using Avalonia.Controls.Documents;
using LiveMarkdown.Avalonia;
using Markdig.Syntax.Inlines;

namespace Claudette.App.Views;

/// <summary>
/// LiveMarkdown's link node, with images left to Claudette (DESIGN.md §18, "Images in Claude's replies"). LiveMarkdown's
/// own loads any image address by itself, from the web or the disk, through a client of its own, so a reply that was
/// prompt-injected could send data out in an image's address just by being shown. Here a link is as LiveMarkdown makes
/// it, and an image is a <see cref="ReplyImageView"/>, which loads only what may. <see cref="App"/> puts it in
/// LiveMarkdown's pipeline in place of <see cref="LinkInlineNode"/>, for every Markdown view.
/// </summary>
public sealed class ReplyLinkInlineNode() : InlinesNode<LinkInline>(new Link())
{
    private ReplyImageView? _image;

    /// <summary>Whether this node is an image or a link; a link that becomes an image, or back, gets a new node.</summary>
    private bool? _isImage;

    protected override bool MatchesInline(LinkInline inline) => base.MatchesInline(inline) && (_isImage is null || _isImage == inline.IsImage);

    protected override bool UpdateCore(DocumentNode documentNode, LinkInline inline, in ObservableStringBuilderChangedEventArgs change, CancellationToken cancellationToken)
    {
        if (inline.Url is null)
        {
            return false;
        }
        _isImage ??= inline.IsImage;
        var link = (Link)Inline;
        if (!inline.IsImage)
        {
            // As LiveMarkdown's own node does; a click goes to the renderer's LinkCommand.
            Uri.TryCreate(inline.Url, UriKind.RelativeOrAbsolute, out var href);
            link.HRef = href;
            return base.UpdateCore(documentNode, inline, in change, cancellationToken);
        }
        // An image isn't a link to follow: its own view handles a click.
        link.HRef = null;
        var alt = string.Concat(inline.FindDescendants<LiteralInline>().Select(l => l.Content.ToString()));
        if (_image is null)
        {
            _image = new ReplyImageView(inline.Url, alt);
            link.Inlines.Add(new InlineUIContainer(_image));
        }
        else
        {
            _image.Show(inline.Url, alt);
        }
        return true;
    }
}
