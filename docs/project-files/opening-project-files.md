---
title: Opening project files
parent: Project files
nav_order: 1
summary: "A Project Files tab shows a project's files beside, or instead of, its conversations: the explorer on one side and the files you've opened, in tabs, on the other."
---

# Opening project files

{% include unreleased.html %}

A Project Files tab shows a project's files beside, or instead of, its conversations: the explorer on one side and the files you've opened, in tabs, on the other. A folder group has at most one.

{% include shot.html name="guide/project-files/files-tab" alt="A project's Files tab: the explorer's tree on the left with letters beside the files Claude changed, and a C++ file open in the editor with its tabs above it and Claude's changes marked in the gutter." %}

## Opening the tab

Any of these opens the group's Files tab, or selects it when it's open:

- The **Project files** button, a file icon beside the group's **+** in the sidebar.
- **Open project files** in the group's menu.
- **Open project files** or **Go to file…** in the [command palette](../tabs/the-command-palette.md).
- `Ctrl/Cmd+P`, which also starts a search ([Finding a file by name](finding-a-file-by-name.md)).
- Opening a file from a conversation ([Opening files from the conversation](opening-files-from-the-conversation.md)).

## Its row in the sidebar

The tab's row is the first in its group, above pinned tabs. It has a file icon where a session tab has its status icon, the name **Project Files**, and on its second line the file showing, or *No files open*. While files have unsaved edits, the second line says how many, such as *2 unsaved*, in the caution color, and the icon has a dot. The collapsed sidebar shows it as a square with the file icon.

Its menu has **Open beside**, **Close pane** (while it's in one), **Reveal in file manager**, **Copy path** and **Close**.

The row stays first. You can't drag it within the group, and **Move up** and **Move down** pass it by.

## How it behaves

- **Next to the conversation.** It takes part in [split panes](../tabs/split-panes.md) like a session tab: choose **Open beside** in its menu, or drag its row onto a pane, to have the conversation on one side and the code on the other. Like every pane, it keeps at least 360 pixels, and inside it the explorer gives way first.
- **Left out of the session tabs' keys.** `Ctrl+Tab`, `Ctrl/Cmd+1` to `9` and `Ctrl/Cmd+J` go only through session tabs and skip it, so opening it never changes their numbers.
- **It keeps its group.** Closing the group's last session tab leaves the group with only its Files tab. **Close group** closes it too.
- **It comes back on launch** with the files it had open, as a pinned tab does.
- **It has nothing a session has.** There's no composer, no side panel (the header's side panel button hides while it has the focus), no status, usage or notifications. Claudette on the composer stays on the session panes.

## What it doesn't do

It has no completion, errors or going to a definition, no search through what's in the files, and no creating, renaming, deleting or moving files. It can't split the editor, and it has box selection but not several cursors. It doesn't preview images, doesn't save by itself, and has no git staging or commits and no terminal.

## Settings

Settings → **Editor** holds this tab's settings, each explained where it applies:

| Setting | Default | Page |
|---|---|---|
| **The explorer is on the** | Left | [The explorer](the-explorer.md) |
| **Wrap long lines** | off | [Editing](editing.md) |
| **Indent new lines with** | 4 spaces | [Editing](editing.md) |
| **Open files in** | Claudette's editor | [Opening files from the conversation](opening-files-from-the-conversation.md) |
| **Show a file clicked in a conversation** | Beside the conversation | [Opening files from the conversation](opening-files-from-the-conversation.md) |

## Shortcuts

| Key | Does |
|---|---|
| `Ctrl/Cmd+P` | Go to a file in the project, which you can rebind in Settings → **Keyboard** |

The editor's own keys are on the pages that use them and in [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## See also

- [Changed files](../changes/changed-files.md) lists what Claude changed in one session, without opening a Files tab.
