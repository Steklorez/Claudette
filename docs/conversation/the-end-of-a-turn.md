---
title: The end of a turn
parent: The conversation
nav_order: 5
summary: "The cards that end a turn: the files it changed, its statistics, where the task list stands and the docs Claude wrote."
---

# The end of a turn

When Claude finishes a turn, the conversation ends it with a few short cards: the files the turn changed, its statistics, where Claude's task list stands, and any Claude Docs it wrote. They save you reading back through the tool calls to see what happened.

{% include shot.html name="guide/conversation/turn-end" alt="The end of a turn: a Modified files card with a tile for each changed file, and a Turn card with its duration, tokens and model." %}

## Modified files
A turn that changed files ends with a **Modified files** card. The header says *3 files changed*. It has a tile for each file the turn changed with Edit, Write, MultiEdit or NotebookEdit, its subagents' changes included. A tile shows the file's name, its counts (`+12 −3`) beside it, and its folder under it. Hover a tile for the full path.

Click a tile to open that turn's changes to the file in the [diff view](../changes/the-diff-view.md): the file as it was before the turn's first change, against how the turn left it. If no later turn changed the file, the diff is against the file as it is now, and the header says so. Changes you made yourself between turns count toward the turn before them. This diff only shows: there is no **Revert** or **Reviewed** in it. Those belong to [Changed files](../changes/changed-files.md), which lists everything since the session started. [Changes per turn](../changes/changes-per-turn.md) has more.

## Turn
A **Turn** card, one quiet line, comes next: how long the turn took, tokens in and out, the model, how much came from the prompt cache, and how many tool calls every agent made. For example: *1m 4s · 75.2k in · 904 out · Opus 5.5 · 62k from cache · 7 tool calls*. The time the turn ended is at the right.

## Tasks

After a turn that changed Claude's task list, a **Tasks** card says where the list stands:

- *✓ All 7 tasks done · 23m*, timed from the first task's start to the turn's end.
- *4 of 7 tasks left · Next: #4 Run the migration*, with **Continue**. It sends *Carry on with the tasks that are left.* as your next message. Only the latest card offers it, until the next turn starts.

Reading the list without changing it doesn't count. [The Tasks page](../plans/the-tasks-page.md) has the full list and the plan.

## Claude Docs

When a turn made, edited or commented on a doc on claude.ai with the Docs connector, a **Claude Doc** card for each doc says so (*Created "Enemies: your task list"*) with **Open**, which opens the doc in your browser. It comes however the turn ended. See [Claude Docs](../plans/claude-docs.md).

## Which turns have them

These cards come from live turns. A restored tab shows none for its earlier turns, because restoring a session doesn't replay each turn's end. A turn that you stopped, or that ended in an error, has no **Modified files** card either: its changes are in Changed files only.

## Settings

The **Turn** card is an in-between step, so **Quieter in-between steps** (Settings → Appearance, on by default) draws it without a tint and outline until you point at it. **Modified files** always keeps its look.

## See also

- [Finding your way](finding-your-way.md): turns that changed files have a mark beside the scroll bar.
- [Notifications](../away/notifications.md): the **Finished** notification starts with the same words as the **Tasks** card.
