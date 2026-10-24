---
title: Settings back to their defaults
parent: Troubleshooting
nav_order: 10
summary: "Claudette keeps its settings in a file, and its tabs and other state in another."
---

# Settings back to their defaults

Claudette keeps its settings in a file, and its tabs and other state in another. If Claudette starts with every setting at its default, one of them couldn't be read. This page says what happens and how to get your settings back. To reset settings on purpose, see [Resetting a section](#resetting-a-section) below.

## Where the files are

| System | Settings (`settings.json`) | State (`state.json`) |
|---|---|---|
| Windows | `%APPDATA%\Claudette` | `%LOCALAPPDATA%\Claudette` |
| macOS | `~/Library/Application Support/Claudette` | `~/Library/Application Support/Claudette` |
| Linux | `~/.config/claudette` | `~/.local/share/claudette` |

The state file holds your saved tabs, recent and favorite folders, the folders you trust and similar things for this computer. Settings → Advanced → **Open data folder** opens the data folder, where the state file is.

Claudette writes each file whole or not at all: it writes a temporary file next to the real one and renames it over the real one. A crash or a power cut leaves the old file or the new one, never half of one.

## A file that isn't valid

If `settings.json` or `state.json` isn't valid JSON, say because someone edited it, Claudette keeps it next to the original as `<name>.<time>.bad` and starts from defaults. Your settings look reset, or your saved tabs don't come back.

To get them back:

1. Quit Claudette.
2. Open the `.bad` file in a text editor and look for what's wrong, such as a missing comma or an end that was cut off.
3. Fix it, and save it as `settings.json` (or `state.json`) in place of the new one Claudette wrote.
4. Start Claudette.

If you'd rather start fresh, leave things as they are and set Claudette up again.

## A file that can't be read

If a file can't be read at all, because something else still has it open after Claudette's retries (an antivirus scanner or a backup, for instance) or because you aren't allowed to read it, Claudette leaves it alone. It starts from defaults for this run, but doesn't save over the file, so your changes this run aren't kept, and your real settings are back the next time Claudette starts.

Close whatever holds the file, or fix its permissions, then restart Claudette.

## Resetting a section

Each section of a Settings page has its own **Reset to defaults**. It returns that section's settings to their defaults and nothing else. A few things stay as they are on purpose:

- **Sessions** keeps the library folder and settings sync, since changing either moves where sessions and settings live.
- **Tabs** keeps your favorite and recent folders, which are data for this computer rather than settings.
- **General** keeps **Start Claudette when I log in**, which the OS holds.
- **Accounts** only sets **New folders use** back to Default. Your accounts stay.

No **Reset to defaults** touches a project's files or the choices Claudette remembers for a project. See [The Settings window](../settings/the-settings-window.md).

To start over completely, quit Claudette and move `settings.json` aside. Claudette makes a new one with the defaults.

## See also

- [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md)
- [Settings sync](../history/settings-sync.md), for settings kept the same on several computers.
