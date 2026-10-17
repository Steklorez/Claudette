---
title: The sidebar
parent: Tabs and sessions
nav_order: 2
summary: "The sidebar lists your tabs down the left of the window, grouped by working folder, so long session names, a status line and many tabs all fit."
---

# The sidebar

The sidebar lists your tabs down the left of the window, grouped by working folder, so long session names, a status line and many tabs all fit. Each tab is one row with two lines.

{% include shot.html name="guide/tabs/sidebar" alt="The sidebar with three folder groups and seven tabs, showing status icons, branches, marks and context rings." %}

## A tab's row

The first line holds the tab's name, with small icons before it. The second line shows the model and effort (for example `Opus · High`), or what needs your attention instead. At the right of the second line are the badges, and at the end of the row is the context ring.

- Click a row to select the tab. Double-click it to [rename it](names-marks-and-pins.md#renaming-a-tab).
- Right-click a row for the [tab's menu](the-tabs-menu.md).
- Hover a row for a moment to see the [tab info card](the-tab-info-card.md).
- The close button shows on the row you hover and on the selected tab.
- A tab in Bypass permissions mode has a thin outline around its row. See [Permission modes](../permissions/permission-modes.md).

## Status icons

The icon at the start of the row says what the tab is doing. Hover it for the words.

| Icon | Meaning |
|---|---|
| `○` | Not started yet, or not running. A restored tab doesn't start its `claude` until you first select it. |
| `●` (pulsing) | Starting, or Claude is working. |
| `!` (highlighted) | Needs your input: a permission prompt, a question or a plan is waiting. |
| `•` | Finished while you were in another tab. It clears when you look at the tab. |
| `✕` | An error, or the process exited. The row's second line says what went wrong. |
| none | Idle. |

A tab's [mark](names-marks-and-pins.md#marks) sits under the status icon, level with the second line.

## The second line

The second line shows the first of these that applies:

- **Needs your input**, or the error, while the tab waits for you or has failed.
- **Possibly stuck**, when two [check-ins](../composer/check-ins-on-long-turns.md) got no reply.
- The task Claude is working on, in its active form (*Running the tests*), when it keeps a [task list](../plans/the-tasks-page.md).
- *Usage limit · continues at 15:45*, while a [usage limit](../usage/when-a-limit-stops-a-task.md) has stopped the task and it will carry on at the reset.
- *Thread · 2 of 3 working*, on a [thread](../threads/making-a-thread.md) while its sub-threads work.
- Otherwise, the model and effort.

## Badges

Small icons before the name and bordered badges at the right of the second line say more about the tab.

| Badge | Meaning | More |
|---|---|---|
| Pin | The tab is pinned. | [Names, marks and pins](names-marks-and-pins.md#pinning-a-tab) |
| Sync arrows | The tab's session is copied to the session library. | [Syncing a tab](../history/syncing-a-tab.md) |
| Thread icon | The tab is a thread. Its sub-threads sit indented under it. | [Making a thread](../threads/making-a-thread.md) |
| Branch icon | The tab works in a git worktree. | [Worktree tabs](worktree-tabs.md) |
| Phone | The tab is connected to the Claude app. It dims while it connects. | [The Claude app](../away/the-claude-app.md) |
| `⚙` | A process the tab started is using CPU. | [Processes](processes.md) |
| A number | Tasks (subagents and background tasks) still running after the turn ended. Hover it for which. | [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md) |
| `3/7` | How many of Claude's tasks are done. Click it to open the Tasks page. | [The Tasks page](../plans/the-tasks-page.md) |
| Branch name | The git branch the folder has checked out, such as `main`. Click it for **Copy branch name**. A worktree tab shows the worktree's name. | [Worktree tabs](worktree-tabs.md) |
| `CL 12345` | The Perforce changelist. | [Perforce](../project-tools/perforce.md) |
| Ring | How full the tab's context window is. It turns amber near auto-compact and red from 95%. Click it to see what fills the context, without selecting the tab. | [Context and compacting](../usage/context-and-compacting.md) |

The ring is hidden until the tab has context data, so a tab that hasn't started has none.

## Account tags

Once you've added an API-key account, a folder group whose new tabs use an account other than Default has a small tag with the account's name beside its label, and a tab whose account differs from its group's has the tag on its row. See [A folder's account](../accounts/a-folders-account.md).

## Threads, subagents and project runs

A thread's sub-threads sit right under it, indented, and move with it.

With Settings → Appearance → **Show subagents' work** set to **Under the tab in the sidebar**, each subagent Claude starts has a small row under its tab, with the agents icon before its task. Click it to see its work in the tab. New in the next release; see [Subagents under their tab](../conversation/subagents.md#subagents-under-their-tab).

Each time one of a tab's project actions runs as a job, such as a build, a small entry appears under the tab's row with how it's going. Click it to show its log where the conversation is, and click the tab's row to go back. See [Project tools](../project-tools/using-project-tools.md).

## The top and the foot

At the top, **New tab** opens the [picker](opening-tabs.md), and a button beside it collapses the sidebar. At the foot are the selected tab's project (a row that opens its menu of actions and links), **History**, the Claude Code and Claudette update badges when there's an update, **New build ready** for a source build, **Settings**, and **User guide**, which opens this guide in your browser.

## Resizing and collapsing

- **Resize** the sidebar by dragging its edge. It's at least 180 pixels wide and 248 by default, and it always leaves the conversation room. Double-click the edge for the default width. The width is remembered.
- **Collapse** it to a rail with the button at its top or `Ctrl/Cmd+B`. The rail shows each group's color, then a square per tab with the first letter of its name, a small status icon at its top right and the tab's mark at its bottom left. Hover a square for the info card. Right-click a group's color bar for the group's menu. The rail doesn't show the context ring or project runs. Whether the sidebar is collapsed is remembered.
- **Narrow windows.** Below 900 pixels wide the sidebar collapses to the rail by itself, and expands again when the window is wider. If you expand it by hand in a narrow window, it stays open until the window is widened.

## Settings

Settings → Appearance:

- **Show context on tab rows**: the ring. On by default.
- **Show git branch on tab rows**: the branch badge. On by default. With it off, a worktree tab shows a branch icon before its name instead.
- **Show task progress on tab rows**: `3/7`, and the task Claude is working on in place of the model while it works. On by default.

Settings → Perforce → **Show changelist on tabs** shows the changelist badge.

## Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl/Cmd+T` | New tab |
| `Ctrl/Cmd+B` | Collapse or expand the sidebar |
| `Ctrl/Cmd+1` to `Ctrl/Cmd+9` | Go to a tab by its place in the sidebar |
| `Ctrl+Tab` and `Ctrl+Shift+Tab` | Next and previous tab |
| `Ctrl/Cmd+J` | Go to the next tab waiting for you |

## See also

- [Icons and badges](../reference/icons-and-badges.md) lists every icon in one place.
- [Folder groups](folder-groups.md) covers the labels the tabs sit under.
