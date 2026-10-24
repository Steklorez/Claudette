---
title: Making a thread
parent: Threads
nav_order: 2
summary: "Everything about threads is in a tab's Thread submenu: right-click the tab in the sidebar, or in the rail when the sidebar is collapsed."
---

# Making a thread

Everything about threads is in a tab's **Thread** submenu: right-click the tab in the sidebar, or in the rail when the sidebar is collapsed. The same commands are in the [command palette](../tabs/the-command-palette.md), where each one's name starts the same way.

## Making a tab a thread

1. Right-click the tab and choose **Thread** → **Make this tab a thread**.
2. Add sub-threads, either way:
   - On the thread, choose **Thread** → **New sub-thread**. A new tab opens in the thread's folder and is assigned to it.
   - Or choose **Thread** → **New sub-thread in a worktree** to give it its own worktree, so its changes stay apart from the thread's checkout ([Worktree tabs](../tabs/worktree-tabs.md)).
   - Or, on another tab in the same folder group, choose **Thread** → **Assign to thread** and pick the thread.

Only tabs in the thread's folder group can join it, including worktree tabs of its checkout. A sub-thread can't be a thread: threads don't nest. The thread's Claude knows each sub-thread by its tab's name; if two have the same name, the second gets `(2)`.

## Changing it later

- **Detach from thread**, on a sub-thread, takes it out of the thread and leaves it an ordinary tab.
- **Go to thread**, on a sub-thread, selects its thread.
- **Stop being a thread**, on a thread, ends it. Its sub-threads carry on as ordinary tabs.
- **Ask before sending to sub-threads**, on a thread, is a switch that's on by default. [Sending work and getting reports](sending-work-and-getting-reports.md) explains it. In the command palette it reads **Stop asking before sending to sub-threads** while it's on.

In the command palette, **Assign to thread: ‹name›** has an entry for each thread you could join.

## In the sidebar

A thread's row has a thread icon before its name. Its tip says how many sub-threads it has, such as *A thread, with 2 sub-threads*. Its sub-threads sit right under it, indented.

Reordering keeps them together: **Move up** and **Move down** on a thread move it, with its sub-threads, past the next tab or thread, and on a sub-thread move it among its thread's. A sub-thread dragged away comes back. When a thread's sub-threads are working and nothing else needs saying, its second line reads *Thread · 2 of 3 working*.

The chevron on the group's colored line, beside a thread's name, folds its sub-threads away under it, with everything under them, and the thread's row then counts them and shows the most urgent, such as `!` for a sub-thread waiting on you. See [Collapsing a tab](../tabs/the-sidebar.md#collapsing-a-tab).

## Showing a thread with its sub-threads

Choose **Thread** → **Show with its sub-threads** on a thread to see it beside its sub-threads: the thread and its first sub-threads, as many as fit, each with its own conversation and composer. The command palette has **Show the thread with its sub-threads**. [Split panes](../tabs/split-panes.md) explains the panes.

## See also

- [What threads are](what-threads-are.md)
- [The tab's menu](../tabs/the-tabs-menu.md): every item in the menu.
