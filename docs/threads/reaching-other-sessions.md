---
title: Reaching other sessions
parent: Threads
nav_order: 5
summary: "Claude Code sessions can message each other by name with SendMessage, which is Claude Code's own tool."
---

# Reaching other sessions

Claude Code sessions can message each other by name with `SendMessage`, which is Claude Code's own tool. Threads use it, and so can any tab: Claude in one tab can send a message to another tab's session, or to a session you have open in a terminal. This page covers how a session is named, how you point Claude at one, and how a message from another session shows up. The sessions and their messaging are Claude Code's; see [its tools reference](https://code.claude.com/docs/en/tools-reference).

## A session's name

A tab's session goes by the tab's name in Claude Code, as long as Settings → Claude Code → **Name each tab's session in Claude Code after the tab** is on ([Names, marks and pins](../tabs/names-marks-and-pins.md) explains it). Without it, Claude Code names a session after its folder with two characters added, like `claudette-3f`, so every tab in a folder looks alike to the others' Claude.

The name a session is reached by can differ from the tab's. Claude Code adds a word to a name another session already has, such as `Art page-joyful-stearns`. The tab's info card shows what it really is:

- **Session** is the session's ID, which `claude --resume` takes.
- **Reached as** is the name other sessions message it by.

Each has **Copy** in the card ([The tab info card](../tabs/the-tab-info-card.md)). The tab's menu has **Copy session ID** once the tab has a session.

## Naming a session in a message

Type `@` in the composer and the list starts with the sessions Claude can message, up to eight, under the header *Sessions and files*. Each is marked with what it is:

- *Sub-thread*: one of a thread's sub-threads, by the name its thread knows it by.
- *Tab*: the session in another of Claudette's tabs, by the tab's name.
- *Session · C:\work\api*: another Claude Code session on this computer, a terminal's or another app's, by its own name and with its folder. Claudette reads Claude Code's list of running sessions each time you start a mention.
- *Subagent · Explore*: one of this tab's subagents, newest first, by its task. `SendMessage` reaches a subagent by its ID.

Type some of a name to narrow the list. Picking one puts `@name ` in the message, or `@"a name" ` when the name has spaces. Names that clash get `(2)`. Files and folders come after in the same list ([Slash commands and mentions](../composer/slash-commands-and-mentions.md)).

When you send, Claude gets the message without the `@`, which Claude Code would take for a file, and a note after it saying who each one is and what to send to: *"Art page" in this message is another Claude Code session on this machine, open in another of Claudette's tabs. To message it, call SendMessage with to: "Art page".* Your message's card keeps what you typed, with *Told Claude how to reach Art page* under it and the note as its tip. A message that starts with a slash command gets no note.

### By session ID

A session's ID, pasted into a message as a word of its own, names it too: a tab's (from **Copy session ID**), a sub-thread's or another session's on this computer. `SendMessage` takes a name, not an ID, so the ID stays in the message and the note gives the name. A tab's own ID names nobody.

## Messages from other sessions

When another session sends a tab a message, the tab shows it as a card labeled *From the session ‹name›* (*From another session* when the sender has no name), and the tab's Claude reads it.

## See also

- [What threads are](what-threads-are.md): the case where a tab's Claude hands work to sub-threads on its own.
- [Slash commands and mentions](../composer/slash-commands-and-mentions.md): the `@` list in full.
