---
title: Reverting
parent: Reviewing changes
nav_order: 4
---

# Reverting

When you've read a change and don't want it, the [diff view](the-diff-view.md) can put the file back as it was, as long as Claudette knows what the file held before.

## Reverting one change

Click **Revert** on the change's header. That undoes just that change and leaves Claude's others.

## Reverting a file

1. Click **Revert file**.
2. The button asks in place: *Put the whole file back?* Click **Revert**, or **Cancel**.

The file goes back to how it was before Claude's first change in the session. A file Claude created is deleted.

## Against HEAD

With **Working tree vs HEAD** on in [Changed files](changed-files.md), the diff is against HEAD, and so is the revert: the file goes back to HEAD, in the line endings it has now, since git keeps its copy in the ones it was committed with.

A file that isn't at HEAD, such as an untracked one that may be your own, has nothing to go back to. Neither revert is offered for it, and it's never deleted.

## When it won't

- If Claude, or anything else, changed the file since the view showed it, nothing is written. The view says *The file changed since this was shown, so nothing was reverted*, and **Refresh** in the **…** menu shows it as it is now.
- Where the file's earlier contents aren't known ([Changed files](changed-files.md#a-file-whose-earlier-contents-arent-known)), there's nothing to put back, so no revert is offered.
- No revert is offered for a binary file or one that's gone.
- A file that isn't valid text in UTF-8, UTF-16 or UTF-32 (Latin-1, say) isn't written, and the view says why. Reading it would mean guessing, and writing the guess back would change characters nobody touched.
- A turn's diffs in [Changes per turn](changes-per-turn.md) have no **Revert**.

## What a revert keeps

A revert keeps the file's line endings, whether it ends with a newline, its byte order mark and its encoding. It writes to a temporary name and renames it over the file, so a crash can't leave half a file. A symbolic link is written through to the file it points to, and stays a link. On macOS and Linux the file keeps its permissions, such as being executable. Changed files picks the change up like any other.

## In a Files tab

{% include unreleased.html %}

The peek under a change in the editor has a **Revert** too, and so does each change in the tab's inline and side by side views. Those change the editor's text instead of writing the file: Undo takes them back, and nothing is written until you save ([Claude's changes in the editor](../project-files/claudes-changes-in-the-editor.md)).

## See also

- [Restore files to before this…](../conversation/rewind-and-branch.md) puts files back to how they were before an earlier message, which is a different thing: it goes back through the conversation, not through one file's changes.
