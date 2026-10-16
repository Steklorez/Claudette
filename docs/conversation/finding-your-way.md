---
title: Finding your way
parent: The conversation
nav_order: 6
summary: "A long conversation can run to thousands of cards."
---

# Finding your way

A long conversation can run to thousands of cards. Claudette marks the ones you are likely to look for, jumps between messages, and has a find bar for the rest.

## Marks beside the scroll bar
While the conversation scrolls, a thin strip beside its scroll bar marks where things are:

- your messages, in the **You** card's color;
- prompts waiting for you, in amber;
- errors, in red;
- turns that changed files, in the **Modified files** card's color.

Each mark sits where its card is in the conversation's height. Hover one to see what it is, and click it to go there. The marks stay put as you scroll. Just after a long conversation opens, or the window changes width, they settle for a moment while Claudette measures the cards out of view. The wheel and clicks reach the conversation through the strip, apart from the marks themselves.

## From message to message
- `F3` goes to the previous message, and `F4` to the next. They count your messages and Claude's replies, starting from the one at the top of the view. One the top of the view cuts off counts as the previous, so going back begins at its own top.
- `Ctrl/Cmd+F3` goes to the first message, and `Ctrl/Cmd+F4` to the latest, as **Jump to latest** does.
- The [command palette](../tabs/the-command-palette.md) has **Go to the first message** and **Go to the latest**.

You can change all four in Settings → Keyboard ([Keyboard](../settings/keyboard.md)).

## Find

**Find in the conversation** (`Ctrl/Cmd+F`) opens a bar over the conversation: a search box, a count such as *3 of 12* (or *No matches*), previous and next buttons, and a close button.

1. Press `Ctrl/Cmd+F` and type. The newest match is selected first, because a conversation is read from the bottom.
2. Press `Enter` for the next match and `Shift+Enter` for the one before. They wrap around.
3. Press `Esc` to close the bar. The focus goes back to the composer.

Find ignores case, and looks through:

- your messages and Claude's replies, and thinking;
- tool calls, with their input and output;
- notes, hook runs and the outcomes of permission cards;
- the rows where tasks start and where a turn left them, and the links and rows that name Claude Docs;
- subagents' groups, with everything in them.

The match is brought into view and marked with the selected-row color. A match in something a collapsed card hides (a tool's output, a thought, a hook's output) opens the card, and one inside a subagent's work opens its group. A match in a card's header leaves it as it is. Each call in a Tool calls card is a match of its own: the card is marked and the call's tile opens.

New output while the bar is open joins the matches without moving the current one. Pressing the shortcut again goes back to the box with its text selected.

To search across sessions, use [History](../history/history.md).

## Shortcuts

| Action | Shortcut |
|---|---|
| Find in the conversation | `Ctrl/Cmd+F` |
| Next match, previous match | `Enter`, `Shift+Enter` |
| Close find | `Esc` |
| Previous message in the conversation | `F3` |
| Next message in the conversation | `F4` |
| First message in the conversation | `Ctrl/Cmd+F3` |
| Latest in the conversation | `Ctrl/Cmd+F4` |

[Keyboard shortcuts](../reference/keyboard-shortcuts.md) lists them all.

## See also

- [Reading the conversation](reading-the-conversation.md) for **Jump to latest**.
- [The command palette](../tabs/the-command-palette.md) for finding a tab or command rather than a message.
