---
title: Advanced
parent: Settings
nav_order: 6
summary: "Settings → Advanced is for the things you reach for when something isn't working, or when you want claude to start with an option Claudette doesn't have a setting for."
---

# Advanced

Settings → Advanced is for the things you reach for when something isn't working, or when you want `claude` to start with an option Claudette doesn't have a setting for. It holds extra arguments for `claude`, protocol logging, two shortcuts to Claudette's folders, and Diagnostics.

## Extra arguments for claude

**Extra arguments for every claude process** takes command-line arguments, such as `--add-dir ../shared`. Claudette splits them on spaces and adds them after its own arguments every time it starts `claude`. They apply to tabs started after you change them, so open a new tab, or close one and open it again, to pick them up.

Claudette already passes the arguments it needs to talk to Claude Code. An extra argument that changes how `claude` talks, such as its input or output format, can stop a tab from working. If a tab stops starting after you add one, empty the box. [Claude Code's CLI reference](https://code.claude.com/docs/en/cli-reference) lists what `claude` takes.

## Log protocol traffic

**Log protocol traffic** writes everything each session sends to and receives from Claude Code to the log folder. It's meant for bug reports: when Claude Code changes, a log shows exactly what changed. It applies to tabs started after you turn it on.

With it on, a message Claudette can't read also appears in the conversation, as a collapsed row, *Unsupported message from Claude Code*, with the raw message inside.

{: .warning }
A protocol log holds your prompts and the contents of files Claude read. Claudette deletes logs after 7 days, but check one before you share it.

## The log and data folders

- **Open log folder** opens the folder with the protocol logs, and with `claudette.log`, the file where Claudette notes its own problems.
- **Open data folder** opens the folder where Claudette keeps its settings, its saved tabs, usage history and drafts.

[Where Claudette keeps things](../privacy/where-claudette-keeps-things.md) lists what's in them.

## Diagnostics

The page's **Diagnostics** section shows how Claudette is getting on with the Claude Code it found:

- **Claude Code** is the installed version, or "Not found".
- **Minimum supported Claude Code version** is the oldest one Claudette works with.
- **Login shell environment** says, on macOS and Linux, whether Claudette used your login shell's environment, which shell, how long it took, or why it didn't. It never shows the variables themselves.
- **Keeping the computer awake** says whether the computer is being kept awake for tabs connected to the Claude app, or why not.
- Under them, counts from this run: the message types Claude Code has sent that Claudette doesn't know, the new fields it has seen, and the lines it couldn't read. Claudette skips what it doesn't know without a problem, so a count here after a Claude Code update isn't an error, but it's worth a bug report.

**Copy diagnostics** copies the same, with Claudette's version, your OS and whether protocol logging is on, for pasting into a bug report. **Refresh** reads them again. [Diagnostics and logs](../troubleshooting/diagnostics-and-logs.md) says more about using them, and [Reporting an issue](../troubleshooting/reporting-an-issue.md) about the report.

## Settings

All of these are in Settings → Advanced, stay on this machine, and can't be overridden per tab. **Reset to defaults** by the **Running claude** heading puts both back.

| Setting | Default |
|---|---|
| **Extra arguments for every claude process** | Empty |
| **Log protocol traffic** | Off |

## See also

- [Every setting](every-setting.md) for the other pages.
- [Claude Code's updates](../updates/claude-codes-updates.md) for the version Claudette found.
