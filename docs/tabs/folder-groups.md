---
title: Folder groups
parent: Tabs and sessions
nav_order: 3
summary: "Tabs that share a working folder sit together in a group in the sidebar, like browser tab groups."
---

# Folder groups

Tabs that share a working folder sit together in a group in the sidebar, like browser tab groups. A group is a label with its tabs listed under it. A folder with a single tab still gets a label, so the sidebar always looks the same.

{% include shot.html name="guide/tabs/sidebar" alt="The sidebar with three folder groups, each with a colored label and its tabs listed under it." %}

## What a group shows

- A **label** with the folder's name and a color. A line in that color runs down beside the group's tabs.
- If two folders have the same name, the label adds the parent folder (`work/api`, `personal/api`).
- Hover the label to see the folder's path and how many tabs the group has.
- At the label's right, the project's icon opens its menu of actions and links ([Project tools](../project-tools/using-project-tools.md)), the **Project files** button its Files tab, and **+** a new tab in the same folder, without the picker.

## Collapsing a group

Click a group's label to collapse it to just the label, and click again to expand it. A collapsed group still shows the most urgent status of its tabs, such as `!` for a tab that needs your input. **Collapse group** and **Expand group** in the group's menu do the same.

## Changing a group's color

A new group takes the first of eight group colors that no open group has.

1. Right-click the group's label, in the full sidebar or the rail, and choose **Project settings…**, then pick **Group**.
2. Under **Saved in**, choose **Shared with the project** (`deventry.json`, for everyone who opens the project) or **Just me** (`deventry.local.json`). Yours shows in place of the shared one, and the page opens on yours when it has a color.
3. Beside **Color**, choose **Choose…**. Pick one of the eight swatches, which closes the picker, or choose any other color with the saturation and brightness square, the hue slider or the hex field (`#12AB34`, or `#1A3`).
4. The group changes color as you drag or type. Press `Enter` to close the picker.

When you close the picker, Deventry saves the color in the file you chose, so with `deventry.json` everyone who opens the project sees its group in that color, on every machine. A folder without the file gets one.

Deventry also remembers the color for the folder on this machine, so the group has it again after a restart, or when you open the folder again after closing its group. **Remove** takes the color out of the file, and the group keeps the color it has, remembered on this machine.

## A group's icon

A project can have a picture of its own in place of the group's colored dot: its logo, say, so you know the group at a glance. The rail shows it too, in place of the group's bar.

1. Right-click the group's label, in the full sidebar or the rail, and choose **Project settings…**, then pick **Group**.
2. Under **Saved in**, choose **Shared with the project** (`deventry.json`, for everyone who opens the project) or **Just me** (`deventry.local.json`). Yours shows in place of the shared one.
3. Choose **Choose…** and pick a PNG, ICO, JPEG, BMP, GIF or WebP of at most 1 MB. One in the project's folder is saved from there, so it works on every machine; one elsewhere is saved by its full path, which only this computer has.

**Remove** takes it out of the file, and the dot comes back. A picture that can't be shown, such as one that's been moved, leaves the dot, and the project's menu says why. Deventry never reads one on a network share.

## Renaming a group

Rename a group when two folders have the same name in different places.

1. Right-click the group's label and choose **Rename…**.
2. Type a name in the box that opens beside the label, which starts with the label selected and the folder's path under it.
3. Choose **Rename**, or press `Enter`. **Cancel**, `Esc`, or a click outside leaves the label as it was.

The label shows your name in place of the folder's. Saving the box empty, or with the folder's own label, goes back to the folder's name, as does **Reset name** in the menu while the group has a name. Deventry remembers the name for the folder on this machine.

## Reordering

Drag a tab to move it within its group. Its row is marked as a selected one is, and the list rearranges as soon as the pointer passes the middle of a neighbor. Drag a group by its label to move the group. A tab can't be dragged into another group, because its folder is fixed, and pinned tabs stay ahead of the others in their group.

**Move up** and **Move down** in a [tab's menu](the-tabs-menu.md) move it from the keyboard or with a click.

Dragging a tab out onto the conversation shows it beside the tab you're in instead: see [Split panes](split-panes.md).

## Pinning a group

With many projects, the ones you work in all the time can be hard to find. Right-click a group's label and choose **Pin to the top** to put the group first in the sidebar, or **Unpin from the top** to let it go. Pinned groups sit in a shaded band of their own at the top, with a line between them and the rest. They keep the order they were in among themselves, and stay ahead of the others. It works on [Claude Code's projects](#claude-codes-projects) too, so a project with nothing open in it can be at the top.

Dragging a group by its label moves it among the pinned groups, or among the others, not from one to the other. The pins are remembered for each folder, on this computer.

## The group's menu

Right-click a group's label, in the full sidebar or the rail. The menu's items are in sections under small titles, as in the header's **≡** menu, with an icon before each. The rail's menu has no **Pin to the top**.

**Tabs**

| Item | What it does |
|---|---|
| **New tab here** | Opens a tab in the group's folder. |
| **New tab in a worktree** | For a folder in a git repository: opens a tab that works in a worktree of its own. See [Worktree tabs](worktree-tabs.md). |

**Folder**

| Item | What it does |
|---|---|
| **Open project files** | Opens the folder's Files tab. See [The Project files button](#the-project-files-button). |
| **History for this folder** | Opens [History](../history/history.md) listing only this folder's sessions. |
| **Project settings…** | Opens the folder's [project settings](../settings/project-settings.md) in their own window: **Links**, **Actions**, **Tools**, and **Group**, where its color and icon are set. Tab or no tab. |

**Group**

| Item | What it does |
|---|---|
| **Collapse group** or **Expand group** | Collapses the group to its label, or expands it. |
| **Pin to the top** or **Unpin from the top** | Pins the group to the top of the sidebar, or lets it go. See [Pinning a group](#pinning-a-group). |
| **Rename…** | Names the group. See above. |
| **Reset name** | Goes back to the folder's name. Shown while the group has a name. |
| **Account ▸** | Picks the account the group's new tabs use. Shown once there's an account besides Default. See [A folder's account](../accounts/a-folders-account.md). |

Then, under a line:

| Item | What it does |
|---|---|
| **Remove from the sidebar** | For one of Claude Code's projects with nothing open in it, in place of **Close group**: takes all of its past sessions off the sidebar at once, and the group with them. See [Removing past sessions](#removing-past-sessions). |
| **Remove past sessions** | The same for a group with tabs open, which stay. Shown while it lists past sessions. |
| **Close group** | Closes the group's tabs, except pinned ones. Not shown while the group is one of Claude Code's projects with nothing open in it. |

**Close group** doesn't touch pinned tabs. See [Closing and restoring](closing-and-restoring.md).

## Claude Code's projects

The sidebar can list every folder Claude Code has sessions in on this machine, so you work in your projects straight from the sidebar, as you would from their folders in a terminal, instead of through the new tab picker or History.

1. Open Settings → Tabs → **Claude Code's projects**.
2. Turn on **Show Claude Code's projects in the sidebar**.

The first time Deventry starts with sessions to show, a card at the top of the sidebar asks instead: **Show them in the sidebar** turns the setting on, and **Not now** leaves it off. Either way it asks only once; the switch is in Settings after that.

With it on:

- **Every folder with sessions is a group**, whether or not a tab is open in it. The ones with nothing open come after the groups with tabs, the most recently used first, and each works like any group: its **+** opens a tab there, its project's icon has the project's menu ([A folder with no tab open](../project-tools/using-project-tools.md#a-folder-with-no-tab-open)), and its menu has **New tab here**, **History for this folder** and the rest. A group with nothing open has no **Close group**; it goes when you turn the setting off, when you [remove its past sessions](#removing-past-sessions), or when its folder no longer exists.
- **Past sessions.** A group with nothing open lists the folder's latest sessions straight away. Once a tab is open in it, they fold away under its tabs into a row such as *12 past sessions*: click it to unfold the latest five, newest first, each with its name and, under it, when it was last used and how many messages it has. Click a session to pick it up in a new tab in the group, as [History](../history/history.md) does. **Show all in History…**, under the five, opens History on the folder when there are more. A session that's open in a tab isn't listed, since its tab is above. Deventry remembers which folders you unfolded.
- The sessions are the ones History lists: Claude Code's own on this machine, including sessions you started in a terminal, and the session library's. Deventry reads them when it starts, when you turn the setting on, and when it comes to the front, so a session started in a terminal meanwhile shows up.

### Removing past sessions

{% include unreleased.html %}

To take a session you're done with off the sidebar, point at it and click the **×** at its right, or right-click it and choose **Remove from the sidebar**. To take all of a folder's at once, right-click the group's label: a group showing only past sessions ends its menu with **Remove from the sidebar**, which takes them all and the group with them. A group with tabs open has **Remove past sessions** there instead, and stays, with its tabs. Removing a group's last past session with its **×** hides the project too.

- Only the sidebar forgets them. [History](../history/history.md) still lists every one, and nothing is deleted.
- A session you use again comes back: open it from History, or pick it up in a terminal or on another computer, and it's listed again, its folder too.
- A new session in the folder brings the group back, with only the new one listed.
- Settings → Tabs → **Claude Code's projects** → **Past sessions removed from the sidebar** brings them all back with **Show 12 again**.
- Deventry remembers what you removed on this computer.

### Settings

- Settings → Tabs → **Claude Code's projects** → **Show Claude Code's projects in the sidebar**: off until you choose. It belongs to this machine, since the folders differ from one machine to the next.
- Settings → Tabs → **Claude Code's projects** → **Past sessions removed from the sidebar**: how many you've removed, with a button to show them again. See [Removing past sessions](#removing-past-sessions).

## The Project files button

A button before a group's **+** (tip: **Project files**) opens that folder's Files tab, an explorer and an editor for the project. It's the same as **Open project files** in the group's menu. See [Opening project files](../project-files/opening-project-files.md).

## The account new tabs use

Once you've added an API-key account in Settings → Accounts, the group's menu has **Account ▸**, which lists Default and each account with its color, the group's ticked. The choice is for the group's new tabs and is remembered for the folder on this machine. Tabs already open keep their account. A group that uses an account other than Default shows a small tag with the account's name beside its label. See [A folder's account](../accounts/a-folders-account.md).

## See also

- [The sidebar](the-sidebar.md), for a tab's row.
- [Names, marks and pins](names-marks-and-pins.md), for naming tabs rather than groups.
