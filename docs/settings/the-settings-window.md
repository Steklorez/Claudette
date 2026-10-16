---
title: The Settings window
parent: Settings
nav_order: 1
summary: "Everything you can change in Claudette is in the Settings window: a list of categories down the left, and the one you pick on the right."
---

# The Settings window

Everything you can change in Claudette is in the Settings window: a list of categories down the left, and the one you pick on the right.

{% include shot.html name="guide/settings/settings-window" alt="The Settings window on General: the categories and a search box down the left with the version, User guide and Report an issue at its foot, and General's settings on the right" %}

## Opening it

Any of these opens Settings:

- The **Settings** button at the foot of the sidebar.
- `Ctrl/Cmd+,`.
- **Settings** in the [command palette](../tabs/the-command-palette.md) (`Ctrl/Cmd+Shift+P`). The palette also lists each category by name, so typing "keyboard" there opens Settings on Keyboard.
- On macOS, **Settings…** in the app menu.

Settings is a window of its own. While it's open you can't use the main window, so close it when you're done.

## The categories

The sidebar lists the categories in this order: General, Sessions, Processes, Claude Code, Accounts, New tabs, Appearance, Usage, Quick suffixes, Check-ins, Diff tool, Editor, Project tools, Notifications, Keyboard, Perforce and Advanced. [Every setting](every-setting.md) has a table for each.

Changes apply as you make them. There's no **Save** button and nothing to restart, except where a setting says it applies to tabs started after the change.

## Searching

Type in **Search settings** above the categories. The list shows each setting that matches, with its category under it. Pick one to open that category. If nothing matches, the list says "No settings match."

The search also finds the selected tab's project pages (below), named with the project: "NightOwl → Links". It matches what's on them, such as "web links", "add an action", "engine" or "editor configuration", and the project's name.

## Reset to defaults

Each category ends with **Reset to defaults**, which puts that category's settings back and nothing else. A few things are left alone, because changing them moves where your data lives or because they're yours rather than settings:

- **General** leaves **Start Claudette when I log in**, which your OS keeps.
- **Sessions** leaves the **Session library folder** and **Sync Claudette's settings through the library**. Use **Use the default folder**, or turn the switch off, when you mean to change them.
- **New tabs** leaves your favorite folders and recent folders.
- **Usage** leaves **Keep usage history**.
- **Accounts** only puts **New folders use** back to Default. Your accounts and their keys stay.
- **Perforce** leaves its per-folder servers and users and any stored password.
- **Appearance** also puts **Zoom** back to 100% and turns **Detailed usage header** off.

No **Reset to defaults** touches a project's files or the choices Claudette remembers for a project, and the project's pages have none.

## The project's pages

Below the categories, after a divider, is a group named for the project of the tab that was selected when you opened Settings: its name when Claudette found one ("NightOwl"), otherwise the folder's name. Hover it for the folder's path. With no tab open, the group isn't there. Each page begins with a line saying which tab it's for.

- **Links** lists the project's web links, from `claudette.json` and `claudette.local.json`, with **Add…**, **Edit…**, **Remove**, **Move up** and **Move down**.
- **Actions** is the same for the project's own actions, with **Open file**.
- **Tools** holds this machine's choices for the project: which project file when the folder has several, its editor configuration or code optimization, and the engine folder, Unity editor or Godot executable you picked, each with **Clear** to forget the pick. Without a project it says "No Unreal, Unity or Godot project in this folder". The defaults every project starts with are in Settings → Project tools.

Each change is saved as you make it, to the file it belongs to, and the project's menu follows at once. Saving rewrites the file, so comments in it are dropped. A file that isn't valid JSON is never rewritten: the page shows why, and you fix the file by hand. [Actions and links](../project-tools/actions-and-links.md) explains what they do. The project's menu has **Add a link…** and **Add an action…**, which open Settings on these pages with a new entry started.

## The version

The foot of the sidebar shows which Claudette this is, such as "Claudette 0.2.0". A copy built from a source checkout adds the commit it was built from.

- **Click the version** to copy the versions for a bug report: Claudette's, and how it was installed, Claude Code's, your OS and the .NET runtime it runs on. It says "Copied" for two seconds. It holds only versions, never paths, names or account details, so it's safe to post.
- **User guide** opens this guide in your browser.
- **Report an issue** opens a new issue on Claudette's GitHub page in your browser, with an outline to fill in and the same versions already there. Nothing is sent until you submit it. [Reporting an issue](../troubleshooting/reporting-an-issue.md) has more.

## Where the settings are kept

Claudette keeps its settings in its own file, separate from Claude Code's. Claudette doesn't change Claude Code's settings except where a page says it does. [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md) says where the file is, and [Settings back to their defaults](../troubleshooting/settings-back-to-their-defaults.md) says what happens if it can't be read.

To keep the same settings on every machine that shares your session library, turn on **Sync Claudette's settings through the library** in Sessions. [Settings sync](../history/settings-sync.md) explains it, and [Every setting](every-setting.md) says which settings it carries.

## Shortcuts

| Action | Shortcut |
|---|---|
| Open Settings | `Ctrl/Cmd+,` |

You can change this one in Settings → [Keyboard](keyboard.md).

## See also

- [Every setting](every-setting.md), a table for each category.
- [Tab settings](../tabs/tab-settings.md), the few settings one tab can change for itself.
