---
title: Where Deventry keeps things
parent: Privacy and your data
nav_order: 1
summary: "Deventry keeps its files in two folders in your user profile: one for its settings and one for everything else, its data."
redirect_from: /privacy/where-claudette-keeps-things/
---

# Where Deventry keeps things

Deventry keeps its files in two folders in your user profile: one for its settings and one for everything else, its data. Claude Code keeps its own files separately.

## The folders

| | Windows | macOS | Linux |
|---|---|---|---|
| **Settings** | `%APPDATA%\Deventry` | `~/Library/Application Support/Deventry` | `~/.config/deventry` |
| **Data** | `%LOCALAPPDATA%\Deventry` | `~/Library/Application Support/Deventry` | `~/.local/share/deventry` |

On Linux, `XDG_CONFIG_HOME` and `XDG_DATA_HOME` move them. The Windows package keeps its files in these same places.

Settings → Advanced → **Open data folder** shows the data folder in your file manager.

To keep a copy of Deventry's files somewhere else, such as for trying things out, start it with the `DEVENTRY_HOME` environment variable set to a folder. Deventry then keeps everything under that folder, in `settings` and `data`, and leaves your real files alone. A copy started that way leaves your login entry alone too ([Starting at login](../away/starting-at-login.md)).

## What's in them

Settings are in `settings.json` in the settings folder. It holds the choices you make in the Settings window. It never holds a password or an API key.

The data folder holds:

| Where | What it is |
|---|---|
| `state.json` | What Deventry remembers about this machine: your open tabs and their order, groups, recent and favorite folders, which folder a session's project is on this machine, and notices you dismissed. |
| `usage.db` | [Usage history](../usage/usage-history.md): plan usage readings, and for each turn the tab, model, token counts and estimated cost. Never your prompts, Claude's replies or any code. It's this machine's own, and kept for as long as Settings → Usage → **Keep usage history** says (1 month by default). |
| `drafts` | Each tab's unsent message, and the stash ([Earlier prompts, drafts and the stash](../composer/earlier-prompts-drafts-and-the-stash.md)). |
| `scratch` | This machine's copy of each [scratch pad](../conversation/the-scratch-pad.md). |
| `library` | The [session library](../history/the-session-library.md), unless you've pointed it somewhere else. |
| `sessions` | Working copies of sessions you opened from the library, which Claude Code continues from. |
| `logs` | `deventry.log`, with Deventry's own warnings and errors, kept under 2 MB (with one older copy). The `protocol` folder inside holds raw logs of what each session sends and receives, only while Settings → Advanced → **Log protocol traffic** is on. They hold your prompts and file contents, and Deventry deletes them after 7 days. **Open log folder** opens the `protocol` folder. |
| `updates` | A Deventry update you've downloaded, until you've installed it. |
| `before-content`, `diff-temp` | Files Deventry keeps so it can show and revert what Claude changed: large files as they were before Claude's first change, and the "before" files an external diff tool is handed. |
| `utility`, `project-jobs`, `builds`, `presence` | Working files: the folder of Deventry's hidden helper session, files that project actions write for Deventry to read, the copies a source build runs from, and a marker that exists while Deventry's window is in front. |
| `restart.json`, `restart-ready` | Files that exist only while Deventry restarts into an update or a new build, to carry your tabs across. |

Unsaved edits in a [Project files](../project-files/saving.md) tab are kept in `editor-backups`, so quitting never loses them. The names and colors of your [API-key accounts](../accounts/api-key-accounts.md) are in `state.json`. Their keys aren't ([Passwords and keys](passwords-and-keys.md)).

Deventry writes its files whole or not at all: to a temporary file, then renamed over the real one. A crash or a power cut leaves the old file or the new one, never half of one. A settings or state file that isn't valid is kept next to it as `<name>.<time>.bad`, and Deventry starts from defaults ([Settings back to their defaults](../troubleshooting/settings-back-to-their-defaults.md)).

## The session library

The library is where a tab that syncs copies its session, in the `library` folder or the one you chose. Each session has its record and a copy of the transcript, which holds your code, command output and anything else Claude read. [The session library](../history/the-session-library.md) lists what's in it and [What leaves this computer](what-leaves-this-computer.md) says when it goes anywhere.

## Claude Code's own files

Claude Code keeps its sign-in, its settings and its transcripts of your sessions in its own folder: `~/.claude`, or the folder `CLAUDE_CONFIG_DIR` names. Each tab runs the real `claude`, so those files are the ones the terminal's `claude` uses.

- Deventry reads the transcripts, for History and to show a session's earlier conversation.
- Deventry never reads or stores your sign-in. Claude Code keeps it, and Deventry only asks Claude Code whether you're signed in.
- Deventry's settings are separate from Claude Code's. Deventry doesn't edit Claude Code's `settings.json` or a project's `.claude` settings, except where a feature says so: **Always allow** on a permission card has Claude Code save a rule to the project's `.claude/settings.local.json` ([Permission cards](../permissions/permission-cards.md)).
- Claude Code deletes its transcripts after 30 days by default, which History can only list as long as they last. Sessions that sync keep their copy in the library.

## See also

- [Passwords and keys](passwords-and-keys.md), which aren't in any of these files.
- [Diagnostics and logs](../troubleshooting/diagnostics-and-logs.md).
