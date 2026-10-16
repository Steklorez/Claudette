---
title: Processes
parent: Tabs and sessions
nav_order: 12
summary: "The process monitor shows what each tab has started, such as test runs, dev servers, builds and MCP servers, with their CPU and memory use."
---

# Processes

The process monitor shows what each tab has started, such as test runs, dev servers, builds and MCP servers, with their CPU and memory use. It's off until you turn it on.

{% include shot.html name="guide/tabs/processes" alt="The Processes page in the side panel, with a tree of a tab's processes and their CPU and memory use." %}

## Turning it on

1. Open Settings → Processes and tick **Show the process monitor**.
2. Or turn it on for one tab only: right-click the tab, choose **Tab settings…**, and set **Process monitor** to **On**. The same setting can turn it off for one tab. See [Tab settings](tab-settings.md).

## What it shows

- **The composer bar's summary**, for example `3 procs · 42% CPU · 1.1 GB`, which leaves out `claude` itself. Click it to open the Processes page. It's hidden while the side panel is open, since the panel shows the same.
- **The activity icon** (`⚙`) on the tab's row while a process it started is using noticeable CPU, 5% or more. See [The sidebar](the-sidebar.md#badges).
- **The header's total**, before the service status dot: the CPU and memory of every tab's processes together, `claude` included (*63% CPU · 2.4 GB*). Hover it for each tab's summary and how many tabs aren't counted. It's hidden while no tab has the monitor on. Claudette's own process isn't in it.
- **The Processes page** in the side panel, a tree of the tab's processes starting from its `claude`.

## The Processes page

Each row has the process's name and PID, its CPU and memory, its running time, and its arguments cut short on one line (a long path is cut to its last part, so `node C:\app\node_modules\vite\bin\vite.js --port 5173` shows `vite.js --port 5173`). Hover a row to see the whole command line.

- **CPU** follows the platform: on Windows, 100% means all cores, as in Task Manager. On macOS and Linux, 100% means one core.
- **Collapsing.** A process with others under it has an arrow before its name. Collapsed, the row counts them after its PID (`+4`), and its CPU and memory include theirs, so collapsing never hides a busy process. Two levels under `claude` show at first, and a process below them starts collapsed. Alt+click (Option+click on macOS) on an arrow expands or collapses every level under it too.
- **A project job** such as a build ([Project tools](../project-tools/using-project-tools.md)) is listed as a top-level process beside `claude`.
- **Link to the conversation.** When a process belongs to a Bash tool call or a background task, its row shows which one. Click it to scroll to that card.
- **Detached processes.** On macOS and Linux, a process that detaches and gets a new parent, such as a daemonized dev server, drops out of the tree. Claudette keeps listing it, marked *detached*, until it exits.

### A row's menu

Right-click a row. It lights up under the pointer and stays lit while its menu is open.

| Item | What it does |
|---|---|
| **Stop…** | Stops the process, after you confirm. Greyed out for `claude` itself. |
| **Copy PID** | Copies its process ID. |
| **Copy command line** | Copies the whole command line. |
| **Reveal executable** | Shows the program in your file manager. |
| **Show in conversation** | Scrolls to the tool call that started it. Greyed out when there's none. |
| **Expand all**, **Collapse all** | On a process with others under it: every level under it. |

## Stopping a process

1. Right-click the process and choose **Stop…**.
2. Read the question and choose **Stop**.

For a background task Claude started, Claude Code stops it and tells Claude it ended. For anything else, Claudette asks the process to close and ends it if it doesn't, and Claude isn't told directly.

## When a tab closes

Claudette tracks each tab's processes even with the monitor off, so closing a tab can stop what it started. If processes are still running, the close question lists them and offers **Close and stop them** or **Close, leave running**. See [Closing and restoring](closing-and-restoring.md).

## Settings

All in Settings → Processes:

- **Show the process monitor**: off by default.
- **Refresh the panel every (seconds)**: how often the page updates while it's showing. The default is 2. The composer bar's summary updates every 10 seconds.
- **Show command lines**: on by default. Command lines sometimes contain tokens or passwords, so turn it off if others can see your screen. Then Claudette doesn't read them, so the rows and their tips don't show them.

## See also

- [The side panel](the-side-panel.md), for the other pages.
- [Tab settings](tab-settings.md), for turning the monitor on for one tab.
