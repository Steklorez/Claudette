---
title: Worktree tabs
parent: Tabs and sessions
nav_order: 11
summary: "Several tabs in one folder edit the same files, so one tab's changes can trip another's."
---

# Worktree tabs

Several tabs in one folder edit the same files, so one tab's changes can trip another's. A tab can work in a git worktree of its own instead: a second checkout of the repository, on its own branch, where its edits don't meet the others'. See [Claude Code's worktrees](https://code.claude.com/docs/en/worktrees) for what a worktree is.

## Opening one

1. Right-click a folder group's label and choose **New tab in a worktree**, or use **New tab in a worktree** in the [command palette](the-command-palette.md) for the selected tab.
2. The tab opens in that folder. Claude Code makes the worktree when it starts, at `.claude/worktrees/<name>` in the repository, on the branch `worktree-<name>`.

Claudette picks the name, two words such as `brisk-otter`, that no other folder, tab or leftover branch there has. The item shows only for a folder in a git repository.

## Working there

From the first turn, the tab's folder is the worktree. Changed files, the git view, `@` files, project tools and the [info card](the-tab-info-card.md) all follow it, and a note says *Working in the worktree brisk-otter, on its own branch*. Later starts resume the session in it.

In the sidebar, a worktree tab stays in the main checkout's group. Its branch badge shows the worktree's name with a branch icon, and its tip names the worktree and its branch. With Settings → Conversation → Tab rows → **Show git branch** turned off, the branch icon sits before the tab's name instead. The info card has a **Worktree** row. See [The sidebar](the-sidebar.md#badges).

**Duplicate tab** and **Branch from here** copy a worktree tab into the same worktree. A session you open from [History](../history/history.md) whose folder is a worktree Claude Code made goes back in its checkout's group, and History names the worktree.

## Claude moving into a worktree

Claude can move a session into a worktree of its own partway through, when you ask it to work in one, and back out again. The tab follows: from then on its folder is the worktree, as a worktree tab's is, it stays in its group, and a note says *Working in the worktree brisk-otter, on its own branch*. When Claude leaves the worktree, the tab goes back, with a note saying *Out of the worktree, back in* and the folder.

A session reopened in a worktree it moved into opens there too. A `cd` into one of the project's folders doesn't move the tab. The files Claude changed in each checkout are grouped in [Changed files](../changes/changed-files.md#several-working-trees).

## Closing the last tab in a worktree

Claude Code leaves a worktree behind, locked, so closing the last tab that works in one asks what to do with it. It asks once that tab's Claude Code has stopped.

- **A worktree with nothing of its own** (no changes, no commits on any other branch) asks **Remove the worktree brisk-otter?** with **Remove** and **Keep**. It says *nothing is lost* only when nothing git ignores is there either. Otherwise it says that such files, such as build output or a local `.env`, go with it.
- **A worktree with work in it** asks **Keep the worktree brisk-otter?** and says what it holds (*uncommitted changes and 2 commits on no other branch*), with **Keep** and **Remove and discard**.

Removing looks again first. If what the worktree holds changed while the question waited, for example a commit made from a terminal, it asks again instead of removing. Removing runs `git worktree remove`, which can take minutes for a big tree, and deletes the `worktree-<name>` branch when it has no commits of its own or you discarded them too. If git refuses, you see what git said.

**Close group** and **Close other tabs** offer the worktrees they leave with nothing of their own together, and keep the rest. A worktree another open tab still works in isn't asked about, and neither is one whose tab you closed with **Close, leave running**. A kept worktree stays with its session in History.

## Setting up a new worktree

A folder's actions marked to run in each new worktree, such as installing dependencies, run there once Claude Code has made it. See [Actions and links](../project-tools/actions-and-links.md).

## Cleaning up worktrees

Kept worktrees pile up. Claudette can remove them on its own. Both rules are off by default.

- Settings → Git → **Worktrees** → **Remove worktrees once everything in them is merged**: a worktree with nothing of its own (no uncommitted changes, and every commit on another branch) goes with its branch, a day after a tab last used it.
- **Remove worktrees no tab has used for**: *Never* (the default), 7, 14, 30 or 90 days. The worktree goes, and its branch stays when it has commits on no other branch, so nothing committed is lost.
- **Clean up now** runs a pass straight away. The settings say what the last pass removed and when (*Last removed brisk-otter and calm-heron, 2 h ago.*).

A cleanup pass never removes a worktree an open tab works in or its session changed files in, one with uncommitted changes or untracked files, or one Claude Code didn't make for a worktree tab. Files git ignores, such as build output, go with a removed worktree. The session stays in History, and opening it finds its folder gone: see [Closing and restoring](closing-and-restoring.md#a-folder-thats-gone).

Claudette looks a minute after it starts and then every hour, while either rule is on.

## Settings

- Settings → Git → **Worktrees**: the two rules above, and **Clean up now**.
- Settings → Git → Tab rows → **Show the git branch**: whether the branch badge shows.

## See also

- [Making a thread](../threads/making-a-thread.md), where **New sub-thread in a worktree** is.
- [Folder groups](folder-groups.md), for the menu with **New tab in a worktree**.
