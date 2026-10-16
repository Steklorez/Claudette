---
title: Rewind and branch
parent: The conversation
nav_order: 10
summary: "You can go back to an earlier message: change it and send it again, start a new tab from just before it, or put the files Claude changed since back as they were."
---

# Rewind and branch

You can go back to an earlier message: change it and send it again, start a new tab from just before it, or put the files Claude changed since back as they were. Claude Code does the work (its [checkpointing documentation](https://code.claude.com/docs/en/checkpointing) describes it), and Claudette gives you the menu.

## The menu

Your messages have a **⋯** button on their card header, shown while you point at the card or it has the keyboard focus. Its menu has **Edit and resend…**, **Branch from here** and **Restore files to before this…**.

## Edit and resend…

The conversation goes back to just before the message, and the message, with its images, goes into the composer for you to change and send.

1. Choose **Edit and resend…** on the message.
2. Read the confirmation. It says how many later messages are left out. If Claude Code kept copies of files from before the message, and some changed since, it also lists them and offers **Go back and restore files** or **Go back, keep files**. Otherwise there is just **Go back**.
3. Change the message and send it.

The tab carries on as a copy of the session, so the session as it was stays in [History](../history/history.md). It works only while Claude isn't working, because going back in the middle of a turn would cut it in half. The menu says so in a note instead.

The first message of a conversation, and the first after `/clear`, has nothing before it, so going back to it starts a new session.

## Branch from here

A new tab in the same folder carries the conversation on from just before the message, with the message in its composer. The tab you branched from is left as it is, and it works while that tab is busy. Branching from the first message opens a new session.

## Restore files to before this…

Claude Code puts the files Claude changed since the message back as they were, and the conversation stays as it is.

1. Choose **Restore files to before this…** on the message.
2. Read the confirmation, which lists the files with their line counts. Later changes to them, Claude's or yours, are lost.
3. Choose **Restore files**.

The tab's `claude` has to be running, and Claude Code has to have kept copies. Without a copy, a note says why. If no files changed since the message, a note says that.

## Duplicate tab

**Duplicate tab**, in the tab's menu ([The tab's menu](../tabs/the-tabs-menu.md)), opens a copy of the whole session in a new tab, or a new session in the same folder for a tab that hasn't had a turn yet. Any copy keeps the tab's settings, its kept quick suffixes, whether it syncs, and which files you marked reviewed.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Keep copies of files Claude changes, so prompts can be rewound** | On | Settings → Claude Code |

It applies to tabs started after you change it.

## See also

- [Reverting](../changes/reverting.md) for undoing one change, or one file, without going back in the conversation.
- [Changed files](../changes/changed-files.md)
