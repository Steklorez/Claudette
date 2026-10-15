namespace Claudette.App.Conversation;

/// <summary>What a mark beside the conversation's scroll bar stands for (DESIGN.md §18, "Conversation cards").</summary>
public enum LandmarkKind
{
    /// <summary>A message the user sent.</summary>
    Prompt,

    /// <summary>A permission prompt, question, plan or MCP server's request still waiting.</summary>
    Waiting,

    /// <summary>An error: Claude Code's, or a hook that failed.</summary>
    Error,

    /// <summary>A turn that changed files.</summary>
    Files,
}

/// <summary>
/// The places in a conversation worth finding again (DESIGN.md §18, "Conversation cards"): marked beside its scroll bar,
/// and the messages F3 and F4 go between.
/// </summary>
public static class ConversationLandmarks
{
    /// <summary>The mark an item gets beside the scroll bar; null for none.</summary>
    public static LandmarkKind? KindOf(ConversationItem item) => item switch
    {
        UserMessageItem => LandmarkKind.Prompt,
        PromptItem { IsPending: true } or McpInputItem { IsPending: true } => LandmarkKind.Waiting,
        NoteItem { IsError: true } or HookRunItem { IsFailed: true } => LandmarkKind.Error,
        TurnSummaryItem { HasFiles: true } => LandmarkKind.Files,
        _ => null,
    };

    /// <summary>What a mark's tooltip says about its item: "Your message: fix the login bug".</summary>
    public static string Describe(ConversationItem item) => item switch
    {
        UserMessageItem user => $"{(user.AutomaticLabel ?? "Your message")}: {OneLine(user.Text)}",
        PromptItem or McpInputItem => "Waiting for your answer",
        NoteItem note => $"Error: {OneLine(note.Text)}",
        HookRunItem hook => $"{hook.Title} failed",
        TurnSummaryItem turn => turn.FilesText,
        _ => "",
    };

    /// <summary>A message F3 and F4 go between: one the user sent, or a reply.</summary>
    public static bool IsMessage(ConversationItem item) => item is UserMessageItem or AssistantTextItem;

    /// <summary>
    /// The message to go to from the item at the top of the view, <paramref name="from"/>: the one before it, or the
    /// one after. -1 when there's none that way.
    /// </summary>
    public static int Step(IReadOnlyList<ConversationItem> items, int from, bool forward)
    {
        if (forward)
        {
            for (var i = Math.Max(-1, from) + 1; i < items.Count; i++)
            {
                if (IsMessage(items[i]))
                {
                    return i;
                }
            }
            return -1;
        }
        for (var i = Math.Min(items.Count, from) - 1; i >= 0; i--)
        {
            if (IsMessage(items[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The first message in the conversation; -1 for none.</summary>
    public static int First(IReadOnlyList<ConversationItem> items) => Step(items, -1, forward: true);

    private static string OneLine(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > 80 ? line[..77] + "…" : line;
    }
}
