---
title: Changes per turn
parent: Reviewing changes
nav_order: 5
summary: "Changed files shows everything since the session started."
---

# Changes per turn

[Changed files](changed-files.md) shows everything since the session started. When you want to know what one turn changed, look at the **Modified files** card at the end of that turn.

{% include shot.html name="guide/conversation/turn-end" alt="The end of a turn: a Modified files card with a tile for each changed file, its line counts beside it, and under it a Turn card with the duration, tokens and model." %}

## The Modified files card

After a turn that changed files, the conversation has a **Modified files** card with *"3 files changed"* in its header and a tile for each file: its name, its folder, and `+12 −3` for the lines added and removed. The counts are from before the turn to how its last change left the file, so another tab editing the same file as the turn ends doesn't change them. A file whose turn changed no line, only what lines don't show, has that in place of the counts, such as *CRLF → LF* or *newline at end*, and pointing at the tile says it in full ([What the lines don't show](changed-files.md#what-the-lines-dont-show)).

The card counts changes Claude made with its file editing tools, subagents' included. Under it, the **Turn** card has the turn's duration, tokens and model ([The end of a turn](../conversation/the-end-of-a-turn.md)).

## Opening a turn's diff

Click a tile. The [diff view](the-diff-view.md) opens with the file as it was before the turn's first change to it, against how the turn's last change left it.

When Claudette can't tell how the turn left the file, the diff is against how the next turn that changed it found it, or the file as it is now, and the line under the title says so: *"this turn's changes: before it, against the file now"*. Changes you made outside Claude between turns then count toward the earlier turn.

This view only shows the changes. It has no **Revert** or **Reviewed**, which belong to the file's changes as Changed files shows them. A file whose earlier contents aren't known is shown as it is, as in Changed files.

## In your diff tool

With an [external diff tool](external-diff-tools.md) set, **Open in diff tool** opens the turn's changes in it: the file before the turn, against how the turn left it, or against the file itself when no later turn changed it. The **…** menu has **Open in the OS's app**, as in Changed files.

## When there's no card

- A restored tab's earlier turns have none, since restoring replays no turn's end.
- A turn you stopped, or that ended in an error, has none. Its changes are in Changed files.
- Going back in the conversation reads it back without these cards ([Rewind and branch](../conversation/rewind-and-branch.md)).

## See also

- [The diff view](the-diff-view.md) for how a diff reads.
- [Reverting](reverting.md) to put a change back.
