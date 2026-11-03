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
- **Clone a repository…** fetches a project that isn't on this machine yet. See [Cloning a repository](#cloning-a-repository).
- **Cancel**, or `Esc`, closes the picker.

## Starting a new project

**New project…** in the picker, or in the command palette, opens a window for a project that doesn't exist yet. **Empty project** makes a new folder: choose the folder to **create in** and type the new folder's **name**. **Start a git repository in it** is on by default, so the folder is a git repository from the start. **Create** makes the folder and opens it as a new tab.

The same window's other choice, **Clone from git**, clones a repository instead, as below.

## Cloning a repository

For a project you don't have a copy of yet, choose **New project…** in the picker and then **Clone from git**, or **Clone a repository…** in the command palette. The window asks for:

- **Repository address**: anything `git clone` takes, such as `https://github.com/org/repo.git`, `git@github.com:org/repo.git` or a path.
- **Clone into**: the folder the clone goes in. It starts as the folder next to the one you last opened a tab in. **Browse…** picks another.
- **Folder name**: filled in from the address (`repo.git` becomes `repo`). Change it if you like; clear it and it follows the address again.

**Clone** runs git and, when it's done, opens the clone as a new tab, in its own group. It uses the git from [Settings → Source control → Git](../settings/git.md), and never asks for a password: a private repository needs your credential helper or SSH key set up, as it would for git on the command line. If the clone fails, the dialog stays open with what git said, and nothing is left on disk.

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
- **From the command line:** `Deventry --folder <path>` opens a tab in that folder when Deventry starts. A relative path is taken from where you ran the command, even when Deventry is already running. See [Command line and environment](../reference/command-line-and-environment.md).
- **On Windows,** the taskbar's jump list shows your favorites and then recent folders, up to 10, leaving out folders that no longer exist. Windows shows nothing there if **Show recently opened items** is turned off in its Start settings.
- **On macOS,** **File → Open Recent** and the Dock icon's menu list the same folders. **File → New Tab** and **File → History…** are there too, and **File → Close Tab** closes the selected tab.

Choosing any of these opens a new tab in that folder.

## One Deventry at a time

Starting Deventry while it's already running doesn't open a second window. The running Deventry comes to the front, and opens a tab if you gave it a folder. Only your own Deventry is handed the request.

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
- [Closing and restoring](closing-and-restoring.md), for what comes back when Deventry starts.
