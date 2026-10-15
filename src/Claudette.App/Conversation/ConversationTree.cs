namespace Claudette.App.Conversation;

/// <summary>
/// Walks a conversation that holds other items: a Tool calls card's tiles and a subagent's group (DESIGN.md §18,
/// "Conversation cards"). Whatever looks through the conversation goes through here, so a card that holds calls
/// can't be missed.
/// </summary>
public static class ConversationTree
{
    /// <summary>Every item in order, each followed by the calls of its card or its subagent's items, however deep.</summary>
    public static IEnumerable<ConversationItem> Descendants(this IEnumerable<ConversationItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            switch (item)
            {
                case ToolGroupItem group:
                    foreach (var call in group.Calls)
                    {
                        yield return call;
                    }
                    break;
                case SubagentItem agent:
                    foreach (var inner in agent.Items.Descendants())
                    {
                        yield return inner;
                    }
                    break;
            }
        }
    }

    /// <summary>The calls at this level, without going into subagents: the tiles of its cards, and its subagents' own calls.</summary>
    public static IEnumerable<ToolUseItem> ToolCalls(this IEnumerable<ConversationItem> items) =>
        items.SelectMany<ConversationItem, ToolUseItem>(item => item switch
        {
            ToolGroupItem group => group.Calls,
            ToolUseItem tool => [tool],
            _ => [],
        });

    /// <summary>
    /// The item at this level that holds <paramref name="item"/>: itself, the card it's a tile in, or the subagent group
    /// it's inside, however deep. Null when it isn't there.
    /// </summary>
    public static ConversationItem? TopLevelOf(this IEnumerable<ConversationItem> items, ConversationItem item) =>
        items.FirstOrDefault(top => ReferenceEquals(top, item) || Holds(top, item));

    /// <summary><paramref name="outer"/> holds <paramref name="item"/>, as a tile or in a subagent's group at any depth.</summary>
    public static bool Holds(ConversationItem outer, ConversationItem item) => outer switch
    {
        ToolGroupItem group => item is ToolUseItem call && ReferenceEquals(call.Group, group),
        SubagentItem agent => agent.Items.Any(child => ReferenceEquals(child, item) || Holds(child, item)),
        _ => false,
    };
}
