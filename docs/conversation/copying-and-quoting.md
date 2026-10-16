---
title: Copying and quoting
parent: The conversation
nav_order: 7
summary: "You can copy a whole message, copy a selection, quote part of a reply back to Claude, or keep a piece of it on the project's scratch pad."
---

# Copying and quoting

You can copy a whole message, copy a selection, quote part of a reply back to Claude, or keep a piece of it on the project's scratch pad.

## Copy a message

A message's card has **Copy message** and **Add to scratch pad** buttons on its header line, before the time. They appear while you point at the card or it has the keyboard focus, so `Tab` reaches them too. After **Copy message**, the button says *Copied* for a moment.

- A reply copies as the Markdown Claude wrote.
- Your own message copies as you sent it, with its quick suffixes after a blank line.

## Copy selected text

Select text in the conversation, in a reply across its Markdown too, and right-click. The menu has:

- **Copy**
- **Quote in reply**
- **Add to scratch pad**

**Quote in reply** puts the selected text at the end of the composer as a Markdown quote, each line starting with `> `, with an empty line under it to write on, and moves the focus there. It is a way to answer one part of what Claude said.

Right-click a message with nothing selected and the menu is about the whole message:

- **Copy message**
- **Quote message**, which quotes the whole message as **Quote in reply** does a selection
- **Add message to scratch pad**
- **Show as the plan**, on a finished reply, which makes the reply the plan on the Tasks page ([The Tasks page](../plans/the-tasks-page.md))

Elsewhere a control keeps its own menu.

## Code blocks

A code block in a reply has a header line with its language and three buttons:

- **Wrap lines** wraps long lines instead of scrolling.
- **Copy** puts the block's code on the clipboard, without the Markdown fences and with your OS's line endings. It says *Copied* for a moment.
- **Add to scratch pad** adds the code to the project's scratch pad, fenced in its language.

Code blocks in the agent map's prompts and reports have **Wrap lines** and **Copy**, but not **Add to scratch pad**, since that window has no pad.

## Your message's menu

A message of yours also has a **⋯** button on its card, with the menu for going back to it. That is [Rewind and branch](rewind-and-branch.md).

## See also

- [The scratch pad](the-scratch-pad.md) for what adding does.
- [Exporting](exporting.md) to save a whole conversation as a file.
- [Reading the conversation](reading-the-conversation.md) for what the cards are.
