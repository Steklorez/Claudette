---
title: Split panes
parent: Tabs and sessions
nav_order: 9
summary: "Show up to three tabs side by side, each pane with its own conversation, composer and working line and a line between them."
---

# Split panes

Show up to three tabs side by side, each pane with its own conversation, composer and working line and a line between them. The panes share the width evenly until you drag a line. Use it to watch a thread and its sub-threads at once, or to keep one tab in view while you work in another.

## Opening a pane

- **Open beside.** Right-click a tab and choose **Open beside**. The tab opens after the focused pane and takes the focus. With three panes already, it takes the place of the last one without the focus. The item shows only for a tab that isn't on screen.
- **Drag a row.** Drag a tab's row from the sidebar onto the conversation. An outline in the accent color marks the half of a pane, or the whole pane, it would take, with a label:
  - *Open beside*: the tab opens next to the pane, on the half you're over.
  - *Move here*: the tab is already on screen and moves to that place.
  - *Open here, in its place*: there's no room for another pane, so the tab takes the pane's place.
  - Dropping a tab on its own pane does nothing. Dropping it back on the sidebar selects it, as a click would.
- **A thread.** **Show with its sub-threads** in a thread's **Thread ▸** menu shows the thread with its first sub-threads, as many as fit. See [Making a thread](../threads/making-a-thread.md).
- **The command palette** has **Show the thread with its sub-threads**.
- **A project action's log.** Right-click a run under its tab's row and choose **Open beside**, or drag the run onto the conversation, to watch a build next to the conversation. See [A run's log](../project-tools/using-project-tools.md#a-runs-log).

A pane needs at least 360 pixels, so how many fit depends on the window. Panes that don't fit fold away, keeping the focused one and its neighbors, and come back as the window widens. Panes sit side by side only: they can't be stacked.

## The focused pane

The selected tab is the focused pane's. The keyboard shortcuts, the command palette, the header (the account, the **Tool windows** menu) and Claudette on the composer all follow the focus.

- Click a pane, or put the cursor in its composer, to focus it.
- Picking a tab in the sidebar focuses it when it's already on a pane. Any other tab takes the focused pane's place.
- Each pane has a header line with the tab's name, its folder and **Close pane**. A bar before the name is in its project's color, the same as its group in the sidebar, and the focused pane's header has that color under it.

Every tab in a pane counts as on screen: its Claude starts, its working line runs, its changed files stay fresh, and it isn't marked unread. Claudette skips its [notifications](../away/notifications.md) while Claudette is in front.

The [tool windows](tool-windows.md) show the focused pane's tab, and name it over them while other tabs are on screen too.

## Resizing

Drag the line between two panes to make one wider and the other narrower. The two trade width, and each keeps at least 360 pixels. The line is drawn in the accent color while you point at or drag it. Double-click a line to share the width evenly again.

Opening or closing a pane shares the width evenly again. Claudette remembers which tabs are in panes and how the width is shared, so they come back when it starts again.

## Closing a pane

**Close pane**, in the pane's header, in the tab's menu or as the command palette's **Close this pane**, takes a tab off the screen and leaves it open in the sidebar. The palette's **Close the other panes** closes all but the focused one. Closing a tab closes its pane. Either way, the pane beside takes the focus if the closed one had it.

## See also

- [The tab's menu](the-tabs-menu.md), where **Open beside** and **Close pane** are.
- [The command palette](the-command-palette.md).
