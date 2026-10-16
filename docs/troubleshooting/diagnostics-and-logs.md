---
title: Diagnostics and logs
parent: Troubleshooting
nav_order: 8
---

# Diagnostics and logs

When something looks wrong, Claudette has a page of diagnostics and two kinds of logs to look at. All of it is in Settings → Advanced.

## Diagnostics

The **Diagnostics** section of Settings → Advanced shows:

- **Claude Code**, the installed version, and **Minimum supported Claude Code version**.
- **Login shell environment**: whether Claudette used your login shell's, and if not, why. See [Claude can't find a program](claude-cant-find-a-program.md).
- **Keeping the computer awake**: whether it's being kept awake, for how many connected tabs, or why not.
- What Claude Code sent this run that Claudette doesn't know yet, in words: the message types it skipped and how many times, the new fields it saw, and how many lines couldn't be read.

Claude Code adds things between versions, and Claudette is built to skip what it doesn't know. Unknown messages and fields are ignored, and a line that can't be read never ends a session. A count here after a Claude Code update is worth a [bug report](reporting-an-issue.md). **Refresh** shows the page as it is now.

**Copy diagnostics** puts a report on the clipboard for a bug report: Claudette's version and how it was installed, the operating system, Claude Code's version and the path it runs from, the minimum version, the login shell line, the Claude app and keep-awake lines, whether protocol logging is on, and the counts above. The path to `claude` can hold your user name, so read the report before you post it.

## In the conversation

- *Claudette couldn't show part of this conversation (…). The session carries on.* A message arrived that Claudette failed to draw. It's logged and the rest of the conversation is unaffected.
- *Unsupported message from Claude Code*, a collapsed row with the raw message in it, shows for messages Claudette skips while protocol logging is on.

## Claudette's log

Claudette always writes its own warnings and errors to `claudette.log`, including anything nothing else caught. It's in the `logs` folder of Claudette's data folder, kept under 2 MB: past that it becomes `claudette.1.log`, replacing the one before. Settings → Advanced → **Open data folder** opens the data folder, which is:

| System | Data folder |
|---|---|
| Windows | `%LOCALAPPDATA%\Claudette` |
| macOS | `~/Library/Application Support/Claudette` |
| Linux | `~/.local/share/claudette` |

## The protocol log

**Log protocol traffic** (off by default) writes everything each session sends and receives, one file per session, to help find out what Claude Code did differently. It applies to tabs started after you turn it on.

- The files are in the `protocol` folder inside `logs`. **Open log folder** opens it.
- They hold your prompts and file contents, and are deleted after 7 days. Don't post one without reading it.
- Sign-in secrets are taken out of each line before it's written: the pasted code and its state, values under the usual names for tokens, keys and passwords, and the `code` and `state` of an address.

Turn it on, make the problem happen again, turn it off, and attach only the part that matters.

## Settings

Settings → Advanced:

- **Log protocol traffic**: off by default.
- **Open log folder** and **Open data folder**.
- **Copy diagnostics** and **Refresh**, under **Diagnostics**.
- **Extra arguments for every claude process**, which are added after Claudette's own to every `claude` it starts, for tabs started after the change. A wrong one can stop tabs starting: clear it when you troubleshoot.

## See also

- [Reporting an issue](reporting-an-issue.md)
- [Advanced](../settings/advanced.md)
- [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md)
