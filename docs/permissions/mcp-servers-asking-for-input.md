---
title: MCP servers asking for input
parent: Permissions and prompts
nav_order: 5
---

# MCP servers asking for input

An MCP server can ask you something in the middle of a turn: a missing detail, a choice, or a sign-in to finish in the browser. This is Claude Code's MCP elicitation (see [Claude Code's MCP docs](https://code.claude.com/docs/en/mcp)). Claudette shows it as a card labeled *Input requested*, and the tab waits on you: its row says *Needs your input*, and a [notification](../away/notifications.md) goes out if Claudette isn't in front.

The card's title is the server's own, or *tickets asks* for a server called `tickets`. What the server says, and every field it asks for, is its text, shown as text.

## A form

The card shows the server's message and a field for each thing it asks for:

- A text box for text and numbers.
- A check box for a yes or no.
- A list for a choice.

A field the server requires says *(required)* in its name, and defaults come filled in. **Send** checks each value against what the server asked for (a whole number, a range, a length, a required field) and says what's wrong under the field, such as *Required.* or *A whole number.* When everything fits, it sends the values to the server.

## A link

Some requests ask you to finish something in the browser, such as signing in to a service.

1. Choose **Open in browser**.
2. Finish in the browser.
3. The card closes by itself when the server says it's done. If it doesn't, choose **Done**.

## Declining

**Decline** tells the server you won't provide it. **Dismiss** cancels the request. The card then says what happened: *Sent*, *Done*, *Declined* or *Dismissed*. A request Claude Code withdrew, or one left unanswered when the session ended, says so.

## From the keyboard

`Ctrl/Cmd+Enter` sends a form as it's filled in, and `Ctrl/Cmd+Backspace` declines the request, as they allow and deny a [permission card](permission-cards.md). A link has to be opened first, so only `Ctrl/Cmd+Backspace` applies to it.

## See also

- [MCP servers](../tabs/mcp-servers.md): the **MCP** page, where each server's status and sign-in live.
