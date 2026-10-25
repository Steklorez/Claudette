---
title: Changed files
parent: Reviewing changes
nav_order: 1
summary: "The Changed files page of a tab's side panel lists the files Claude changed in that tab's session, each added, modified or deleted, with how many lines changed."
---

# Changed files

The **Changed files** page of a tab's side panel lists the files Claude changed in that tab's session, each added, modified or deleted, with how many lines changed. It's built from Claude's file edits, live while the tab works and again when a conversation is restored.

{% include shot.html name="guide/changes/changed-files" alt="The Changed files page in the side panel: a summary of five files changed with two reviewed, a Working tree vs HEAD box, and a row for each file with its status letter, name, line counts and folder." %}

## Opening the page

- Click **Files (n)** in the composer bar, where n is how many files are listed.
- Or open the side panel and pick the **Changed files** page ([The side panel](../tabs/the-side-panel.md)).
- Or choose **Show changed files** in the [command palette](../tabs/the-command-palette.md).

Each tab has its own list. A tab in the background only keeps its count, and lists its files once you select it. `/clear` starts a new conversation, and the list starts again with it.

## Reading a row

Each row has, from the left:

- a box to tick when you've reviewed the file ([Reviewed files](reviewed-files.md));
- a letter: **A** for a file Claude created, **M** for one it modified, **D** for one deleted, and **=** for a file that's back to how it was;
- the file's name, with `+12 −3` beside it for the lines added and removed, and its folder under it. A file that isn't text shows *binary* in place of the counts;
- when something changed that its lines don't show, such as its line endings, a line saying what ([What the lines don't show](#what-the-lines-dont-show));
- when the file has changed on disk since this session's last change to it, a line saying so ([Changed since](#changed-since)).

Point at a row to see what it is in words.

The summary at the top reads *"5 files changed · 2 reviewed"*. **Refresh** reads the files again.

## This session's changes

The counts are this tab's own: from what the file held before Claude's first change in the session to how Claude's latest change left it. Several tabs often work in one folder, and their Claudes change the same files; another tab's edits, yours and a command's don't count as this session's. A file Claude put back as it was shows as unchanged.

## What the lines don't show

A diff compares lines, so some changes look the same on both sides. When one of them is the only change, or comes with others, the row says what it was under its folder:

- *"Only its line endings changed: CRLF to LF"*: the file's lines end differently. Claude's **Write** saves a file with Unix line endings (LF), so a file that had Windows ones (CRLF) changes all through. An edit to a file with mixed line endings makes them all CRLF.
- *"Only its byte order mark changed: removed"* (or *added*): the invisible mark some editors put at the start of a file, as in many Unity and Unreal projects.
- *"Only the newline at its end changed: removed"* (or *added*).
- *"Only whitespace changed"*: lines changed only in their spaces and tabs, which the [diff view](the-diff-view.md#what-the-lines-dont-show) shows.

When lines changed too, it reads *"Its line endings changed too: CRLF to LF"*, and two or more at once read like *"Only its line endings (CRLF to LF) and the newline at its end (removed) changed"*. Such a file counts as changed, and **=** is only for a file that's back to how it was in every way.

Claudette reads a file's line endings just before Claude changes it, since Claude Code doesn't report them. For a session it didn't run, such as one opened from History, it doesn't have them, and a change to them isn't told. To change a file's line endings back, open it in its project's Files tab, click the line endings on its chip and choose **CRLF** or **LF** ([The chip](../project-files/editing.md#the-chip)).

## Changed since

When a file on disk isn't as this session's last change left it, its row says so under its folder:

- *"Changed since: +3 −1"*: lines added and removed since, by you, a command or something else.
- *"Changed since: +3 −1, by Claude in Fix login"*: another tab's Claude changed it after this one, named by the tab (*"by Claude in Fix login and 1 other tab"* when more did).
- *"Deleted since"*: the file's gone. A file Claude created that's gone again counts as back to how it was.

A row whose tip adds *"with changes made outside this session between its edits"* was changed by something else between two of this session's edits, so its counts have that change in them too.

The list keeps up by itself:

- while the tab is on screen, a file Claude changed is looked at again a moment after it changes on disk;
- when another tab's Claude changes a file this tab lists;
- when a turn ends, for what its commands did;
- when you come back to Claudette from another app.

A tab off screen catches up once you show it.

## Several working trees

A session can change files in more than one checkout of a repository: a worktree Claude moved into ([Worktree tabs](../tabs/worktree-tabs.md#claude-moving-into-a-worktree)), one a subagent works in, or one made with `git worktree add` beside the checkout. The list then has a group for each, with the tab's own first, each headed by its folder and branch, such as *"brisk-otter · worktree-brisk-otter"*. A file's folder is shown from its own checkout's top.

When a worktree is removed, its files stay in their group, headed *"brisk-otter · removed"*, with Claude's changes as they were, and **Open** isn't offered for them. While a tab's Claude has changed files in a worktree, cleaning up worktrees leaves it ([Worktree tabs](../tabs/worktree-tabs.md#cleaning-up-worktrees)).

## Working tree vs HEAD

In a git repository, the **Working tree vs HEAD** box switches the list to everything git sees as changed in the whole repository, not only the tab's folder:

- changes made by commands Claude ran, and by you;
- new files git doesn't ignore, which count as added;
- files git sees as deleted or renamed (**D** and **R**).

It also lists each other checkout Claude changed files in, in its own group ([Several working trees](#several-working-trees)).

A file whose lines git sees as changed only at their ends says *"Only its line endings changed"*, with what to what when git can tell, or *"they differ from HEAD's"*, as for a file checked out with other line endings than git would commit. Its counts leave those lines out, as its diff does. A file whose only change is that it's now executable, or no longer, says *"Only its mode changed"*.

Claudette asks git half a second after Claude's edits pause, not after each one, and again when a turn ends, when you come back to Claudette, and, while the tab is on screen, when you stage, commit or check out, from a terminal too. It never takes git's lock on your repository's index, so it won't get in the way of your own git commands. Outside a git repository the box isn't there.

## A file whose earlier contents aren't known

Claude Code reports a file's earlier contents as it changes it. For a session Claudette didn't run live, such as one opened from History, it may not have them. The file is then listed as modified without counts, and its row's tip says Claude Code didn't report the earlier content. Its [diff](the-diff-view.md) shows the file as it is now and says so.

Claude Code leaves a file's earlier contents out of a saved conversation when the file is over 10,000 characters. So while Claudette runs a session, it keeps its own copy of those, and a restored tab or a session opened again from History finds it. Copies not used for 30 days are deleted.

Claude Code doesn't report the earlier contents of a file a subagent changes at all, so Claudette reads the file just before the subagent changes it, and keeps that too.

## Opening a file's changes

Click a row, or press `Enter` on it, to open its [diff view](the-diff-view.md). The arrow keys move through the list without opening anything. Right-click a row for **Open diff**, **Open in diff tool**, **Open**, **Open in the OS's app**, **Reveal in file manager** and **Copy path**, which [The diff view](the-diff-view.md#the-files-actions) explains, and **Diff tool settings…**, which opens Settings → **Files and diffs** at the [diff tool](external-diff-tools.md).

With an [external diff tool](external-diff-tools.md) set, a single click only selects the row, and a double-click opens the tool.

## Settings

- Settings → **Files and diffs** → **Diff tool**: how a double-click opens a file ([External diff tools](external-diff-tools.md)).
- Settings → **Files and diffs** → **Open files in**: where **Open** goes ([Opening files from the conversation](../project-files/opening-files-from-the-conversation.md)).

## See also

- [Changes per turn](changes-per-turn.md) lists only what one turn changed.
- In a project's Files tab, **Show only changed files** lists what Claude changed in the explorer ([The explorer](../project-files/the-explorer.md)).
