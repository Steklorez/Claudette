using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// An image in a reply's Markdown (DESIGN.md §18, "Images in Claude's replies"), which <see cref="ReplyLinkInlineNode"/>
/// puts where LiveMarkdown would have put its own. It learns which tab it's in once it's shown, from the first ancestor
/// whose data is a tab (the conversation, the side panel, the agent map's window).
/// </summary>
public partial class ReplyImageView : UserControl
{
    public ReplyImageView()
    {
        InitializeComponent();
        // The Link span it sits in underlines everything in it.
        SetValue(Inline.TextDecorationsProperty, null);
    }

    public ReplyImageView(string address, string altText)
        : this() => DataContext = new ReplyImageViewModel(address, altText);

    public ReplyImageViewModel? ViewModel => DataContext as ReplyImageViewModel;

    /// <summary>The reply streamed on and the image's address or alt text changed: it starts again from them.</summary>
    public void Show(string address, string altText)
    {
        if (ViewModel is { } current && current.Address == address && current.AltText == altText)
        {
            return;
        }
        DataContext = new ReplyImageViewModel(address, altText);
        if (TopLevel.GetTopLevel(this) is not null)
        {
            AttachToTab();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachToTab();
    }

    private void AttachToTab()
    {
        var tab = this.GetVisualAncestors().OfType<StyledElement>().Select(a => a.DataContext).OfType<TabViewModel>().FirstOrDefault();
        ViewModel?.Attach(tab?.ReplyImages, tab is null ? null : image => tab.OpenImageCommand.Execute(image));
    }
}
