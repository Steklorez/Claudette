---
title: Opening files from the conversation
parent: Project files
nav_order: 8
summary: "When Claude names a file, or you click a file in Changed files, the file opens in the project's Files tab, at the line that's named."
---

# Opening files from the conversation

When Claude names a file, or you click a file in Changed files, the file opens in the project's Files tab, at the line that's named. Claude's replies, a tool call's details and Changed files all reach the editor this way.

## Where a file opens from

- A file named in Claude's replies ([Files and links in replies](../conversation/files-and-links-in-replies.md)).
- **Open** in Changed files, and in the diff window, which then closes so it doesn't hide the editor ([The diff view](../changes/the-diff-view.md#opening-the-file)). Settings → **Editor** → **Open in the diff window** can send the diff window's elsewhere.
- **Open** in the details of a Read, Edit, Write or NotebookEdit call's tile, beside **Open diff** ([Tool calls](../conversation/tool-calls.md)).

Each of their menus keeps **Open in the OS's app**.

## At the line

A line named in a reply (`Player.cpp:42`, `:42:7` or `#L42-L51`) is gone to, and a range is selected. An Edit call's file opens at the first line its change touched.

## Which Files tab

The Files tab of the group whose folder holds the file, or the deepest group when one's folder is inside another's. A file in no group's folder, such as `~/.claude/settings.json`, opens in the Files tab of the tab you clicked in. Its file tab shows its whole path, and the explorer doesn't list it.

## Where it shows

A click in a conversation opens the Files tab beside it, as **Open beside** does ([Split panes](../tabs/split-panes.md)), unless it's on screen already. The Files tab takes the focus, so the keyboard goes to the editor.

Settings → **Editor** → **Show a file clicked in a conversation** can change that to **In its place**: the Files tab then takes the conversation's pane, as selecting it in the sidebar does.

When the Files tab opens beside the conversation for its first file, it starts with its explorer hidden, so the file has the pane. That's this Files tab alone, and it's kept with it: the other Files tabs keep theirs. The explorer's button, **Find file** and **Reveal in the explorer** bring it back, and so do a folder named in a reply and the group's **Project files** button ([The explorer](the-explorer.md#hiding-it)).

Close the file when you're done with it, and the Files tab closes too: the conversation has its width back. If you showed the explorer meanwhile, the Files tab stays beside the conversation ([File tabs](file-tabs.md#closing)).

## What still goes elsewhere

- A program or shortcut that isn't text (`.exe`, `.lnk`, `.app`, `.desktop`) still shows in its folder, since opening it would run it.
- Binary files, and files over 10 MB, open in the OS's app.
- An image opens in Claudette's image window, as Settings → **Editor** → **Open images in** says ([Images](../conversation/images.md#opening-an-image-claude-names)).
- A folder in the project shows in the explorer, expanded and selected. One elsewhere opens in Explorer, Finder or your file manager.
- A script (`.bat`, `.ps1`, `.sh`, `.py`, `.js`) opens in the editor as text, since showing its text doesn't run it.

## Using the OS's app instead

Settings → **Editor** → **Open files in** has two choices, **Claudette's editor, at the line named** (the default) and **The app the OS uses for the file**. With the second, every file goes where it went before the Files tab existed, and **Show a file clicked in a conversation** turns off, since it only applies to the editor.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Open files in** | **Claudette's editor, at the line named** | Settings → **Editor** |
| **Show a file clicked in a conversation** | **Beside the conversation** (or **In its place**) | Settings → **Editor** |
| **Open images in** | **Claudette's image viewer** (or **The app the OS uses for the file**) | Settings → **Editor**, explained in [Images](../conversation/images.md#settings) |

## Shortcuts

`Ctrl/Cmd+P` opens the project's Files tab with a search ready, beside the conversation or in its place by the same setting ([Finding a file by name](finding-a-file-by-name.md)).

## See also

- [Opening project files](opening-project-files.md) for the other ways to open the tab.
