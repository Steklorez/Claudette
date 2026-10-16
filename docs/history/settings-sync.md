---
title: Settings sync
parent: History and syncing
nav_order: 5
---

# Settings sync

Settings sync keeps Claudette's settings the same on every machine that uses the same [session library](the-session-library.md) folder. Change your theme on one machine and the others follow.

## Turning it on

1. Open Settings → Sessions.
2. Check **Sync Claudette's settings through the library**. It's off by default.

If the library already holds settings from another machine, Claudette asks: *The library already has settings from another machine. Which should this machine use?*

- **Use synced settings** takes the library's settings, and every page shows them.
- **Replace them with this machine's** publishes this machine's settings to the library instead.
- **Cancel** leaves sync off.

Unchecking it keeps the settings as they are on this machine and stops syncing.

## What syncs

- Appearance, except **Zoom**, which suits each machine's screen.
- New-tab defaults.
- Usage settings: the warning levels, continuing when a limit resets, and **Share usage with my other machines**.
- Check-ins.
- Quick suffixes.
- Notifications.
- Keyboard shortcuts, and **Send with**.
- Process monitor options.

## What stays on each machine

- Whether Claudette starts at login, and the rest of Settings → General.
- The path to `claude`, the login shell setting, and the Claude app settings (Settings → Claude Code).
- This page's own settings: the machine's name, the library folder, how long library sessions are kept, and whether new tabs sync.
- The diff tool, Settings → Editor, Settings → Project tools, and Perforce settings. Program paths and installed tools differ between machines, and a Perforce server, workspace and stored password belong to the machine.
- Accounts and their keys.
- Recent and favorite folders, folder mappings, pinned tabs, and window sizes and positions.

A stored Perforce password is never in a settings file at all. See [Passwords and keys](../privacy/passwords-and-keys.md).

[Every setting](../settings/every-setting.md) says, for each setting, whether it syncs.

## How it merges

Claudette compares settings at launch, after each change you make, and once a minute to pick up changes from other machines. Each setting keeps the time it was last changed, and the newest change wins, so edits on two machines don't overwrite each other wholesale. A change on one machine and a different one on another both survive. When both changed the same setting, the newer one wins. Lists, such as quick suffixes, sync as one value.

The synced settings are one file in the library, `settings-sync.json`.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Sync Claudette's settings through the library** | Off | Settings → Sessions |

**Reset to defaults** on the Sessions page leaves it as it is.

## See also

- [The session library](the-session-library.md) for the folder the settings travel through.
- [What leaves this computer](../privacy/what-leaves-this-computer.md).
