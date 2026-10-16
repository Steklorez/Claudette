---
title: The side panel
parent: Tabs and sessions
nav_order: 8
summary: "The side panel sits at the right of the conversation."
---

# The side panel

The side panel sits at the right of the conversation. It has a page for the files Claude changed, the agents, the project's tools and more, shown as tabs along its top. The page showing has its name in full text with an accent line under it, and the others are muted.

{% include shot.html name="standard" alt="The main window with the side panel open on Changed Files, beside a conversation." %}

## Opening and closing it

Whether the panel is open is each tab's own choice, and it starts closed. To show or hide it for the selected tab:

- Click the button at the far right of the header, with the sidebar's icon mirrored (**Show the side panel** or **Hide the side panel**).
- Click **Files** in the composer bar, which opens the panel on Changed Files.
- Click the **×** at the end of the page tabs to close it.

Some things open it for you: the composer bar's **Agents** button opens it on Agents, and the command palette's **Show changed files**, **Show the agent map**, **Show tasks**, **Show processes**, **Show MCP servers** and **Show scratch pad** open it on that page.

## The pages

| Page | When it shows | More |
|---|---|---|
| **Changed Files** | Always. | [Changed files](../changes/changed-files.md) |
| **Agents** | Always. A busy dot shows while subagents are active. | [The agent map](../plans/the-agent-map.md) |
| **Project** | When the folder holds a project Claudette has tools for. A busy dot shows while a job runs. | [Project tools](../project-tools/using-project-tools.md) |
| **Processes** | When the process monitor is on for the tab. | [Processes](processes.md) |
| **Tasks** | Once Claude keeps a task list or a plan. A count shows beside the name. | [The Tasks page](../plans/the-tasks-page.md) |
| **MCP** | When the session has MCP servers. A warning dot shows when one needs attention. | [MCP servers](mcp-servers.md) |
| **Scratch Pad** | Always. | [The scratch pad](../conversation/the-scratch-pad.md) |
| **Games** | While a game is open. | [Below](#the-games-page) |

A page's tab shows a busy dot while it has something running. When the tabs don't all fit, the last ones go into the menu of the **⋯** button after the row, which has a dot while one of them does. The page showing always keeps its tab in the row.

Each page scrolls and wraps its text at the width shown, so a long file name or an error is never cut off.

## Reordering the pages

The order is the same for every tab, and it's remembered.

- **Drag** a page's tab along the row. The others make way as the pointer passes their middle.
- **Right-click** a page's tab for **Move left**, **Move right** and **Reset order**.

A page added in a later version goes after the page it follows by default.

## Resizing

Drag the panel's left edge to make it wider or narrower. It's at least 260 pixels wide and 340 by default, and the conversation always keeps at least 360 pixels between it and the sidebar. Double-click the edge for the default width. The width is the same for every tab, and remembered.

When the window narrows, the side panel gives way first, down to 260, and only then the sidebar. The widths you dragged come back as the window widens.

With tabs [side by side](split-panes.md), the side panel shows only on the focused pane.

## The Games page

{% include unreleased.html %}

While you play a game against Claudette, it opens on a **Games** page. See [Games with Claudette](../claudette/games-with-claudette.md).

## See also

- [The composer bar](../composer/the-composer-bar.md), where the **Files** count is.
- [The sidebar](the-sidebar.md), for the other panel.
