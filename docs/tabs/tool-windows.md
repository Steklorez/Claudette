---
title: Tool windows
parent: Tabs and sessions
nav_order: 8
summary: "Changed files, the agents, tasks and more are tool windows at the window's right and along its bottom, docked beside the conversation or folded into tabs that slide out over it."
redirect_from: /tabs/the-side-panel/
---

# Tool windows

{% include unreleased.html %}

Changed files, the agent map, tasks and the rest are tool windows, as in Visual Studio. They sit at the right of the window and along its bottom, outside the conversation, and there's one set for the whole window. They used to be the side panel's pages, which each tab had of its own.

{% include shot.html name="standard" alt="The main window with the Changed files tool window slid out over the conversation, and the right edge's tabs along the window's edge." %}

## What they show

They show the tab you're looking at: its changed files, its agents, its tasks. With a [Files tab](../project-files/opening-project-files.md) or a log in front, they show the session tab of that group you used last, and its name shows over them, with its group's color. The same happens with tabs [side by side](split-panes.md), where the windows follow the pane with the focus.

| Window | When it has a tab | More |
|---|---|---|
| **Changed files** | Always. | [Changed files](../changes/changed-files.md) |
| **Agents** | Always. A busy dot shows while subagents are active. | [The agent map](../plans/the-agent-map.md) |
| **Project** | When the folder holds a project Claudette has tools for. A busy dot shows while a job runs. | [Project tools](../project-tools/using-project-tools.md) |
| **Processes** | When the process monitor is on for the tab. | [Processes](processes.md) |
| **Tasks** | Once Claude keeps a task list or a plan. A count, such as *2 of 5*, shows beside the name. | [The Tasks page](../plans/the-tasks-page.md) |
| **MCP** | When the session has MCP servers. A warning dot shows when one needs attention. | [MCP servers](mcp-servers.md) |
| **Scratch pad** | Always. | [The scratch pad](../conversation/the-scratch-pad.md) |
| **Games** | While a game is open. | [Games with Claudette](../claudette/games-with-claudette.md) |

Each window scrolls and wraps its text at the width shown, so a long file name or an error is never cut off.

## Docked or auto-hidden

Each edge of the window, the right and the bottom, is in one of two states. The pin on its header switches between them.

- **Auto-hidden**: its windows fold into tabs along the window's edge. Point at a tab for a moment, and its window slides out over the conversation; it slides back once the pointer leaves it. Click a tab, and the window stays out until you click elsewhere or press `Esc`. The right edge starts this way, so its tabs are there without taking the conversation's room.
- **Docked**: the edge takes its own room beside the conversation, with its windows as tabs along its header. Click a tab to show its window.

Drag a docked edge's inner side to make it wider or taller; double-click it for the default. The right edge is at least 260 pixels wide, 340 by default, and the conversation always keeps at least 360 pixels. When the window narrows, a docked right edge gives way first, then the sidebar.

## Showing and hiding them

- The **Tool windows** button at the far right of the header lists every window, ticked while it's on screen. Choose one to show it, or to hide it.
- **Files** in the composer bar shows Changed files, and the bar's **Agents** button, process summary and task progress show theirs.
- The command palette's **Show changed files**, **Show the agent map**, **Show tasks**, **Show processes**, **Show MCP servers** and **Show scratch pad** show that window.
- The **×** on an edge's header closes the window showing. A closed window comes back where it was the next time something shows it.

## Moving them

- **Drag** a window's tab along its edge's tabs to change their order.
- **Drag** it to the other edge: the place it would go is marked, as when you drag a tab onto a pane, with what it would do, such as *Dock bottom* or *Tab with Changed files*. Drop it to dock it there. `Esc` cancels.
- **Right-click** a window's tab for **Move to the bottom** (or **Move to the right**) and **Close**.

Where each window is, and each edge's size and state, are remembered.

## See also

- [The composer bar](../composer/the-composer-bar.md), where the **Files** count is.
- [The sidebar](the-sidebar.md), the window's other panel.
