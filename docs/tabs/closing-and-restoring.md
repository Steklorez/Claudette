---
title: Closing and restoring
parent: Tabs and sessions
nav_order: 14
summary: "Closing a tab stops its Claude Code, and Claudette asks first when something would be lost."
---

# Closing and restoring

Closing a tab stops its Claude Code, and Claudette asks first when something would be lost. When Claudette starts again, pinned tabs come back and resume their sessions, and so do the others unless you turned that off.

## Closing a tab

Click the **×** on the tab's row, press `Ctrl/Cmd+W`, or choose **Close** in the tab's menu. A tab that's idle closes at once. Otherwise a question, *Close "‹the tab's name›"?*, lists what would be lost:

- *This tab is pinned; closing it also unpins it.*
- *Claude is still working in it and will be stopped.* This one is asked only while Settings → General → **Confirm before closing a tab where Claude is working** is on, which it is by default.
- *It started 2 processes that are still running: …*, naming up to five.

Choose **Close** to go ahead, or **Cancel**. When there are processes running, the choices are **Close and stop them** and **Close, leave running**. Claudette lets `claude` finish its transcript first, then ends whatever it left behind, unless you chose to leave it running. See [Processes](processes.md).

Closing the last tab that works in a git worktree asks what to do with the worktree: see [Worktree tabs](worktree-tabs.md).

## Closing several tabs

- **Close other tabs**, in a tab's menu, closes every other tab except pinned ones.
- **Close group**, in a group's menu, closes the group's tabs except pinned ones. See [Folder groups](folder-groups.md).

Neither asks first, and both skip pinned tabs.

## Pinned tabs

A pinned tab comes back every time Claudette starts and resumes its session. Pin one with **Pin tab** in its menu: see [Names, marks and pins](names-marks-and-pins.md#pinning-a-tab). Pinned tabs belong to this machine. On another machine, the same sessions appear in [History](../history/history.md) instead.

## Unpinned tabs

Unpinned tabs that were open when Claudette quit come back only while Settings → Sessions → **Also restore unpinned tabs when Claudette starts** is on. It's on by default for a new install, so every tab comes back at launch. Settings saved by 0.2.0 or earlier had it off and keep it off until you turn it on, or use Sessions' **Reset to defaults**.

## What comes back

For each tab Claudette restores:

- Its folder, name, mark, pinned state, whether it syncs, and its place in its group.
- Its session, model, effort and the settings it has of its own.
- The [suffixes](../composer/quick-suffixes.md) kept on it and its token counts.
- The message you hadn't sent yet.
- Which changed files you ticked as reviewed, and whether every one is.

Tabs come back in the same order and resume their sessions, with the earlier conversation loaded so you can scroll back.

### Tabs that start when selected

A restored tab doesn't start its `claude` until you first select it or send it a message, so launching with many tabs is quick and tabs you don't touch use no resources. Until then its row shows `○`. The earlier conversation is read back a line at a time, off the main thread, so a long session doesn't hold the window up.

## A folder that's gone

If a restored tab's folder no longer exists, for example a deleted clone, the tab says so as soon as it's restored: *The folder … no longer exists. If it moved, choose where it is now and the session carries on there.* It offers:

- **Choose folder…**, for a folder that moved or another clone of the same project. The tab moves to the chosen folder's group and its session carries on there.
- **Unpin and close**, or **Close tab** for a tab that isn't pinned.

## A session Claude Code deleted

Claude Code deletes local transcripts after 30 days by default, so a pinned tab you haven't used in a while could lose its. If the tab syncs, or once did, Claudette resumes from its [session library](../history/the-session-library.md) copy, which that cleanup doesn't touch.

If neither copy exists, the tab says so (*The earlier conversation couldn't be found here or in the session library*) and waits. It never starts a new session by itself. Choose **Start a new session** to start one in the same folder, keeping the tab's name, pinned state, settings and suffixes, or **Unpin and close** (**Close tab**) to close it.

## When Claude Code stops

If a tab's `claude` stops, its row and the status line under the composer say so, with the reason when there is one, and the status line has a **Restart** button. **Restart Claude Code** in the [command palette](the-command-palette.md) does the same. See [A tab that won't start](../troubleshooting/a-tab-that-wont-start.md) for the reasons a tab gives.

## Quitting Claudette

Closing the Claudette window interrupts running turns and stops each tab's `claude` and everything it started. When Claudette restarts itself into a new build or version, it keeps every tab and draft: see [Claudette's updates](../updates/claudettes-updates.md).

## Settings

- Settings → Sessions → **Also restore unpinned tabs when Claudette starts**: see above.
- Settings → General → **Confirm before closing a tab where Claude is working**: on by default.

## Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl/Cmd+W` | Close the selected tab |

## See also

- [The tab's menu](the-tabs-menu.md), for **Close** and **Close other tabs**.
- [A tab that won't start](../troubleshooting/a-tab-that-wont-start.md).
