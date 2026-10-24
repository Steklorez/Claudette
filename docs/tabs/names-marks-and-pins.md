---
title: Names, marks and pins
parent: Tabs and sessions
nav_order: 4
summary: "Three small things set a tab apart in the sidebar: its name, an icon of your own (a mark), and whether it's pinned."
---

# Names, marks and pins

Three small things set a tab apart in the sidebar: its name, an icon of your own (a mark), and whether it's pinned.

## How a tab is named

A new tab shows its folder's name. After your first message, Claude Code gives the session a short title, the same kind of name it shows as a terminal tab title, and the tab takes it. Claude Code's title never replaces a name you chose.

## Renaming a tab

1. Double-click the tab's row, or right-click it and choose **Rename**.
2. Type the new name.
3. Press `Enter` to save it. `Esc` leaves the name as it was. Clicking elsewhere saves it.

To go back to the automatic name, choose **Reset name** in the tab's menu, which shows while the tab has a name of yours. Saving the name box empty, or with the automatic name, does the same.

A copy of a tab ([Duplicate tab](the-tabs-menu.md#duplicate-tab)) doesn't take your name.

## The name in Claude Code

Other Claude Code sessions message a session by its name, and `claude --resume <name>` in a terminal finds it. With Settings → Claude Code → **Name each tab's session in Claude Code after the tab** on, the tab's name, yours or the generated one, is the session's name there too. It follows a rename, and it's on by default for a new install. A settings file saved by an earlier version keeps the choice it had.

Without it, Claude Code names a session after its folder with two characters added (`claudette-3f`), so every tab in a folder looks alike to the others' Claude.

If another session already has a tab's name, Claude Code adds to it. The [info card](the-tab-info-card.md)'s **Reached as** row shows the name other sessions use. See [Reaching other sessions](../threads/reaching-other-sessions.md).

## Marks

A mark is a small icon of your own on a tab, meaning whatever you want it to. Claudette gives it no meaning.

1. Right-click the tab and open **Mark ▸**.
2. Pick one: **Check**, **Cross**, **Question mark**, **Star**, **Flag** or **Pause**.

The mark shows under the tab's status icon, level with the second line: a green check, a red cross, an amber question mark, a gold star, a flag in the accent color and a muted pause sign. Hover it for its name.

The menu ticks the tab's mark, and picking it again takes it off. **Clear mark**, in the menu while the tab has one, does the same.

A mark belongs to the session. It's saved with the tab, so it comes back after a restart, and for a tab that [syncs](../history/syncing-a-tab.md) it goes to your other machines. [History](../history/history.md) shows it before the session's title, and opening the session brings it back. A copy of a tab starts without the mark, and after `/clear` the tab keeps it.

## The reviewed icon

While every file Claude changed in the session is ticked as reviewed, the row shows a reviewed icon in the mark's place: a green disc with a check cut out of it. Its tip says so, with the mark it covers (*All changed files reviewed · Marked with a star*).

It isn't a mark, and it isn't in the menu. The tab's own mark stays as it was and shows again as soon as a file isn't reviewed, when Claude changes one or you untick one. On the rail, it shows in the mark's corner. History shows only the mark. See [Reviewed files](../changes/reviewed-files.md).

## Pinning a tab

Pin a tab you want back every time Claudette starts.

1. Right-click the tab and choose **Pin tab**.
2. To undo it, choose **Unpin tab** in the same place.

A pinned tab shows a pin icon and sits at the start of its folder group. It comes back at every launch and resumes its session. **Close group** and **Close other tabs** leave it open. Closing it asks first, and unpins it. See [Closing and restoring](closing-and-restoring.md).

## Settings

- Settings → Claude Code → **Name each tab's session in Claude Code after the tab**: on by default for a new install.
- Settings → General → **Also restore unpinned tabs when Claudette starts**, which decides whether tabs you haven't pinned come back too. See [Closing and restoring](closing-and-restoring.md#unpinned-tabs).

## See also

- [Folder groups](folder-groups.md), for naming and coloring a group.
- [The tab's menu](the-tabs-menu.md), which lists every item on a tab's right-click menu.
