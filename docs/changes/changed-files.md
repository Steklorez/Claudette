---
title: Changed files
parent: Reviewing changes
nav_order: 1
---

# Changed files

The **Changed Files** page of a tab's side panel lists the files Claude changed in that tab's session, each added, modified or deleted, with how many lines changed. It's built from Claude's file edits, live while the tab works and again when a conversation is restored.

{% include shot.html name="guide/changes/changed-files" alt="The Changed Files page in the side panel: a summary of five files changed with two reviewed, a Working tree vs HEAD box, and a row for each file with its status letter, name, line counts and folder." %}

## Opening the page

- Click **Files (n)** in the composer bar, where n is how many files are listed.
- Or open the side panel and pick the **Changed Files** page ([The side panel](../tabs/the-side-panel.md)).
- Or choose **Show changed files** in the [command palette](../tabs/the-command-palette.md).

Each tab has its own list. A tab in the background only keeps its count, and lists its files once you select it.

## Reading a row

Each row has, from the left:

- a box to tick when you've reviewed the file ([Reviewed files](reviewed-files.md));
- a letter: **A** for a file Claude created, **M** for one it modified, **D** for one deleted, and **=** for a file that's back to how it was;
- the file's name, with `+12 −3` beside it for the lines added and removed, and its folder under it. A file that isn't text shows *binary* in place of the counts.

Point at a row to see what it is in words.

The summary at the top reads *"5 files changed · 2 reviewed"*. **Refresh** reads the files again.

The counts compare what the file held before Claude's first change in the session with the file on disk now. So edits you make later show up too, and a file that's back as it was shows as unchanged.

## Working tree vs HEAD

In a git repository, the **Working tree vs HEAD** box switches the list to everything git sees as changed in the whole repository, not only the tab's folder:

- changes made by commands Claude ran, and by you;
- new files git doesn't ignore, which count as added;
- files git sees as deleted or renamed (**D** and **R**).

Claudette asks git half a second after Claude's edits pause, not after each one, and it never takes git's lock on your repository's index, so it won't get in the way of your own git commands.

## A file whose earlier contents aren't known

Claude Code reports a file's earlier contents as it changes it. For a session Claudette didn't run live, such as one opened from History, it may not have them. The file is then listed as modified without counts, and its row's tip says Claude Code didn't report the earlier content. Its [diff](the-diff-view.md) shows the file as it is now and says so.

Claude Code leaves a file's earlier contents out of a saved conversation when the file is over 10,000 characters. So while Claudette runs a session, it keeps its own copy of those, and a restored tab or a session opened again from History finds it. Copies not used for 30 days are deleted.

## Opening a file's changes

Click a row, or press `Enter` on it, to open its [diff view](the-diff-view.md). The arrow keys move through the list without opening anything. Right-click a row for **Open diff**, **Open in diff tool**, **Open**, **Open in the OS's app**, **Reveal in folder** and **Copy path**, which [The diff view](the-diff-view.md#the-files-actions) explains.

With an [external diff tool](external-diff-tools.md) set, a single click only selects the row, and a double-click opens the tool.

## Settings

- Settings → **Diff tool**: how a double-click opens a file ([External diff tools](external-diff-tools.md)).
- Settings → **Editor** → **Open files in**: where **Open** goes ([Opening files from the conversation](../project-files/opening-files-from-the-conversation.md)).

## See also

- [Changes per turn](changes-per-turn.md) lists only what one turn changed.
- In a project's Files tab, **Show only changed files** lists what Claude changed in the explorer ([The explorer](../project-files/the-explorer.md)).
