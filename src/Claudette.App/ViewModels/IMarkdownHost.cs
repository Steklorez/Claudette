using Claudette.App.Conversation;
using Claudette.Core.Protocol;

namespace Claudette.App.ViewModels;

/// <summary>
/// What Markdown shown in Claudette needs from where it shows: the images it may load and the window they open in
/// (DESIGN.md §18, "Images in Claude's replies"), and Copy on its code blocks (DESIGN.md §5, "Copy and times"). A session's
/// tab is one, for its replies, plans and agents, and so is a Markdown file's preview (DESIGN.md §18, "Markdown preview").
/// </summary>
public interface IMarkdownHost
{
    ReplyImages ReplyImages { get; }

    /// <summary>A click on an image: full size in a window of its own.</summary>
    void ShowImage(ToolImage image);

    /// <summary>Puts a code block's code on the clipboard, and says "Copied" on it for a moment.</summary>
    Task CopyCodeAsync(string code, object block, Action<bool> showCopied);
}
