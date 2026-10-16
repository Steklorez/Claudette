---
title: Claude Code's updates
parent: Updates
nav_order: 1
summary: "How Claude Code updates depends on how it was installed."
---

# Claude Code's updates

How Claude Code updates depends on how it was installed. Claudette looks at how yours was, tells you when a newer version is there, and runs the update for you when it can. It never restarts a tab to apply one.

## The badge

A small badge appears at the foot of the sidebar: *Claude Code 2.1.290 is ready*. With the sidebar collapsed it's an icon. It appears when:

- your package manager has a newer version than the one installed, or
- the installed version is newer than the one an open tab is running.

Choose it to open a card with the current and the new version, **What's new in Claude Code** (a link to Claude Code's changelog), and the actions that apply:

- **Update now** runs the update and shows its output in the card. When it works, the card reports the new version.
- **Update on next launch**, for installs that can't be replaced while a tab runs them (see the table). It runs the update the next time Claudette starts, before any tab starts. **Don't update on next launch** takes it back.
- **Dismiss** hides the badge until a newer version comes along. It stays hidden across restarts, and goes away by itself once no open tab runs an older version.

Settings → Claude Code shows the same details and actions, plus the install method, whether Claude Code updates itself, its channel, the last update attempt, and any warnings `claude doctor` found, with the fix it suggests.

## What **Update now** does

| How Claude Code was installed | How it updates | What Claudette does |
|---|---|---|
| The native installer | Updates itself in the background. The new version is used the next time a `claude` starts. | Tells you an update is installed. **Update now** runs `claude update`. |
| npm | Updates itself, if the npm global folder is writable. | The same. If it can't update, the card shows the fix `claude doctor` suggests. |
| Homebrew | Doesn't update itself. | **Update now** runs `brew upgrade claude-code`, or `claude-code@latest` for that cask. |
| WinGet | Doesn't update itself. | **Update now** runs `winget upgrade Anthropic.ClaudeCode`. Windows locks a running program, so this fails while a tab runs it, and **Update on next launch** takes its place. |
| apt, dnf or apk | Needs administrator rights. | Shows the command, with **Copy**. Run it in a terminal; Claudette doesn't. |

Claude Code's own docs describe [its installation and updates](https://code.claude.com/docs/en/setup).

## Tabs keep their version

Open tabs are never restarted. Each tab's `claude` keeps the version it started with until you close the tab.

- **New tabs** start on the newly installed version.
- **To move an open tab** to it, close the tab and open a new one, or reopen the session from [History](../history/history.md).
- **Pinned tabs** pick it up the next time Claudette launches, because restored tabs start new processes.

A tab running an older version says so in its tooltip: *Running Claude Code 2.1.284; 2.1.290 is installed. New tabs use 2.1.290.*

## When Claudette checks

At launch and every 4 hours, Claudette asks Claude Code for its version and for what `claude doctor` reports. For Homebrew and WinGet it also asks the package manager what's newer. A native install has no way to ask, so Claudette relies on Claude Code's own updater and notices the new version.

- **Check for Claude Code updates automatically** in Settings → Claude Code turns the checks off. **Check now** there runs one.
- If updates are turned off in Claude Code itself (`DISABLE_UPDATES`, set directly or by managed settings), Claudette shows the version and doesn't offer to update.

## A version that's too old

Claudette needs Claude Code {{ site.claude_code.minimum }} or later. With an older one, the setup screen offers **Update now** before any tab starts: see [The first launch](../getting-started/the-first-launch.md) and [Claude Code missing or too old](../troubleshooting/claude-code-missing-or-too-old.md).

## Settings

- Settings → Claude Code → **Check for Claude Code updates automatically**: on by default.

## See also

- [Claudette's updates](claudettes-updates.md), for updating Claudette itself.
- [How Claudette uses Claude Code](../getting-started/how-claudette-uses-claude-code.md).
