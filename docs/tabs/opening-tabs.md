---
title: Opening tabs
parent: Tabs and sessions
nav_order: 1
summary: "Every tab has a working folder, which you pick when you open it."
---

# Opening tabs

Every tab has a working folder, which you pick when you open it. Several tabs can use the same folder. The new tab picker lists the folders you use, and there are other quick ways in.

{% include shot.html name="guide/getting-started/new-tab" alt="The new tab picker open over the window, with favorite and recent folders listed." %}

## The new tab picker

1. Press `Ctrl/Cmd+T`, or choose **New tab** at the top of the sidebar.
2. Type to narrow the list. The search box matches folder names and paths.
3. Pick a folder with the arrow keys and `Enter`, by double-clicking it, or with `1` to `9` for one of the first nine.

A tab opens in the folder you picked, in that folder's group in the sidebar.

Each entry shows the folder's name and its path (shortened with `~`), its current git branch when it's a repository, when you last used it, and how many tabs are open in it now (for example *2 h ago · 1 tab open*). A folder that no longer exists is dimmed and marked *not found*.

Favorites are listed first, then recent folders, most recent first.

At the bottom of the picker:

- **Browse…** opens the system's folder picker, for any other folder.
- **Open from History…** opens [History](../history/history.md), to resume a past session instead of starting a new one. Its shortcut is `Ctrl/Cmd+Shift+H`.
- **Cancel**, or `Esc`, closes the picker.

## Favorites and recent folders

Right-click a folder in the picker for:

- **Add to favorites** or **Remove from favorites**.
- **Remove from recents**.
- **Reveal in file manager**.
- **Copy path**.

A folder is added to the recent list, or moved to the top, whenever a tab opens in it. Favorites never drop off the list. Both lists belong to this machine, because paths differ from one machine to the next, so they don't sync with your other machines.

## Other ways to open a tab

- **A group's +** opens a tab in that group's folder without the picker. See [Folder groups](folder-groups.md).
- **Drop a folder** from Finder or Explorer onto the sidebar.
- **From the command line:** `Claudette --folder <path>` opens a tab in that folder when Claudette starts. A relative path is taken from where you ran the command, even when Claudette is already running. See [Command line and environment](../reference/command-line-and-environment.md).
- **On Windows,** the taskbar's jump list shows your favorites and then recent folders, up to 10, leaving out folders that no longer exist. Windows shows nothing there if **Show recently opened items** is turned off in its Start settings.
- **On macOS,** **File → Open Recent** and the Dock icon's menu list the same folders. **File → New Tab** and **File → History…** are there too, and **File → Close Tab** closes the selected tab.

Choosing any of these opens a new tab in that folder.

## One Claudette at a time

Starting Claudette while it's already running doesn't open a second window. The running Claudette comes to the front, and opens a tab if you gave it a folder. Only your own Claudette is handed the request.

## Settings

All in Settings → Tabs → New tab picker:

- **Recent folders to keep**: how many recent folders the picker remembers. The default is 20.
- **Clear recent folders**: empties the recent list. Favorites are kept.
- **Favorite folders**: your favorites in order, with **Add folder…**, a move up and move down arrow and **Remove** for each.

The **Model**, **Effort** and **Permission mode** under **New tabs start with**, on the same page, are what a new tab starts with. A tab can have its own: see [Tab settings](tab-settings.md).

## Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl/Cmd+T` | Open the new tab picker |
| `Ctrl/Cmd+Shift+H` | Open from History |
| `Up` and `Down`, `Enter` | Choose a folder in the picker, and open it |
| `1` to `9` | Open one of the first nine folders in the picker |
| `Esc` | Close the picker |

## See also

- [Your first tab](../getting-started/your-first-tab.md), a walk through opening a tab and sending a message.
- [Closing and restoring](closing-and-restoring.md), for what comes back when Claudette starts.
