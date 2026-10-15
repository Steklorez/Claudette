using Claudette.App.Conversation;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>Find in the conversation (DESIGN.md §5, "Find"): the bar over the conversation, and the match it shows.</summary>
public sealed partial class TabViewModel
{
    public ConversationSearch Find { get; }

    /// <summary>Asks the view to put the focus in the find bar's box, with its text selected.</summary>
    public event Action? FindFocusRequested;

    /// <summary>
    /// F3 and F4, Ctrl/Cmd+F3 and Ctrl/Cmd+F4, or whatever Settings → Keyboard says (DESIGN.md §18, "Conversation cards"):
    /// asks the view to go to the message before or after the one at the top of the view, the first, or the latest.
    /// </summary>
    public event Action<MessageJump>? MessageJumpRequested;

    public void JumpToMessage(MessageJump jump) => MessageJumpRequested?.Invoke(jump);

    /// <summary>Ctrl/Cmd+F, or whatever Settings → Keyboard says: opens the bar, or goes back to it.</summary>
    [RelayCommand]
    private void OpenFind()
    {
        Find.IsOpen = true;
        FindFocusRequested?.Invoke();
    }

    /// <summary>The match the bar moved to: opened if a collapsed card hides it, and brought into view.</summary>
    private void OnFindCurrentChanged(ConversationItem item)
    {
        if (Find.IsHidden(item))
        {
            switch (item)
            {
                case ThinkingItem thinking:
                    thinking.IsExpanded = true;
                    break;
                case ToolUseItem tool:
                    tool.IsExpanded = true;
                    break;
                case HookRunItem hook:
                    hook.IsExpanded = true;
                    break;
                case UserMessageItem user:
                    user.ShowsAllText = true;
                    break;
            }
        }
        ScrollTo(item);
    }
}

/// <summary>Where F3 and F4 go in the conversation (DESIGN.md §18, "Conversation cards").</summary>
public enum MessageJump
{
    Previous,
    Next,
    First,
    Latest,
}
