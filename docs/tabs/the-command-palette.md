---
title: The command palette
parent: Tabs and sessions
nav_order: 10
---

# The command palette

The command palette is a box over the window that does anything Claudette has a command for, when you type a few letters of it. It also jumps to a tab, opens a folder and goes to a Settings page.

{% include shot.html name="guide/tabs/command-palette" alt="The command palette open over the window, with a list of commands and their shortcuts." %}

## Using it

1. Press `Ctrl/Cmd+Shift+P`.
2. Type a few letters of what you want. The box says *Type a command, a tab, a folder or a setting*.
3. Choose with `Up` and `Down`, and press `Enter` to run it. Double-clicking an entry runs it too.
4. Press `Esc`, click outside it, or press the shortcut again to close it without running anything.

While the palette is open, the window's other shortcuts are left to it.

## What it lists

Before you type, the list has four kinds of entry, in this order. The kind shows beside each entry: **Command**, **Tab**, **Folder** or **Settings**.

- **Commands.** Each shows its shortcut, so the palette teaches them.
- **Tabs**, by name with their folder. Picking one selects it.
- **Folders**: your favorites, then recent folders. Picking one opens a new tab there.
- **Settings** pages. Picking one opens Settings there.

### The commands

For the whole window:

- **New tab**, **Open from History**, **Settings**
- **Collapse the sidebar** or **Expand the sidebar**
- **Zoom in**, **Zoom out**, and **Reset zoom** while zoomed
- **Go to the next tab waiting for you**, when another tab is waiting

For the selected tab:

- **Find in the conversation**
- **Stop Claude**, while Claude works
- The project's main action, named for it (such as *Build*), with the project's name beside it
- **Stash the message**, while something's typed in the composer
- **Show changed files**, **Show the agent map**, **Show tasks**, **Show processes**, **Show MCP servers** (when the session has some) and **Show scratch pad**
- **Tab settings…**, **Duplicate tab** and **Export conversation…**
- **New tab in a worktree**, for a folder in a git repository
- The thread commands that apply: **Make this tab a thread**, **New sub-thread**, **New sub-thread in a worktree**, **Ask before sending to sub-threads** (or **Stop asking before sending to sub-threads**), **Stop being a thread**, **Assign to thread:** and a thread's name, **Go to thread** and **Detach from thread**
- **Restart Claude Code**, when it has stopped
- **Close tab**

## Filtering

An entry matches when the letters you typed appear in its name in order, ignoring case. A match at the start of the name or of a word, or with the letters together, comes first. An entry also matches by its kind, so typing `tab` finds tabs, a little lower down.

## Going to the next tab waiting for you

`Ctrl/Cmd+J` selects the next tab, in sidebar order after the selected one and going round, that's waiting for you: a permission prompt, a question or a plan. Nothing happens when no other tab is waiting. The palette has the same command, **Go to the next tab waiting for you**, while one is.

## New in the next release

{% include unreleased.html %}

Some commands come with features that no release has yet:

- **Go to file…** and **Open project files**, which open the project's Files tab. While a Files tab has the focus, the palette has its own commands for saving, closing a file, going to a line, showing only changed files, hiding the explorer and closing the tab. See [Opening project files](../project-files/opening-project-files.md).
- **Close this pane**, **Close the other panes** and **Show the thread with its sub-threads**, with tabs [side by side](split-panes.md).
- **Go to the first message** and **Go to the latest**, which jump through the conversation.
- **Play tic-tac-toe with Claudette**, **Play Connect Four with Claudette** and **Play hangman with Claudette**. See [Games with Claudette](../claudette/games-with-claudette.md).

## Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl/Cmd+Shift+P` | Open or close the palette |
| `Up` and `Down` | Choose an entry |
| `Enter` | Run the chosen entry |
| `Esc` | Close the palette |
| `Ctrl/Cmd+J` | Go to the next tab waiting for you |

## See also

- [Keyboard shortcuts](../reference/keyboard-shortcuts.md), which lists every shortcut.
- [The tab's menu](the-tabs-menu.md), for what a right-click offers.
