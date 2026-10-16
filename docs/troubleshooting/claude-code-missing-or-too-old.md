---
title: Claude Code missing or too old
parent: Troubleshooting
nav_order: 1
---

# Claude Code missing or too old

Claudette runs your installed Claude Code, so without one it can't start a tab. When Claude Code is missing, too old or won't answer, Claudette shows a setup screen before anything else. [The first launch](../getting-started/the-first-launch.md) describes the screen; this page is for fixing what it says.

## Claude Code isn't installed

Claudette found no `claude` on your `PATH`, or no file at the path you gave it. The screen's text says which.

1. Install Claude Code with [Claude Code's setup instructions](https://code.claude.com/docs/en/setup). **Install instructions** on the screen opens them.
2. Choose **Check again**.

If Claude Code is installed but Claudette can't find it:

- Choose **Choose claude…** and pick the `claude` program. Claudette remembers it in Settings → Claude Code → **Path to claude**.
- On macOS and Linux, a Claudette started from the Dock or a launcher doesn't see the `PATH` your terminal does. It reads your login shell's, so check that your shell's startup files add the folder `claude` is in: see [Claude can't find a program](claude-cant-find-a-program.md).
- If the screen says a path *doesn't exist*, the path in Settings points at a file that's gone. Choose **Choose claude…** and pick the right file.

## Claude Code needs an update

The screen says which version Claudette found. Claudette needs {{ site.claude_code.minimum }} or later.

- Choose **Update now**. It runs the update for the way Claude Code was installed and shows the output. When it works, Claudette checks again by itself.
- If **Update now** isn't offered, or fails, update Claude Code yourself and choose **Check again**. [Claude Code's updates](../updates/claude-codes-updates.md) has the command for each way of installing it.

## Claude Code didn't respond

Claudette runs `claude --version` to read the version. The screen says what went wrong: the command failed, ran too long, or printed something that isn't a version. Run `claude --version` in a terminal. If that fails too, Claude Code itself needs repairing: reinstall it. If it works, choose **Check again**, or **Choose claude…** if a different `claude` should be used.

## This Claude Code install isn't supported yet

On Windows, Claude Code installed with npm can leave only a `claude.cmd` launcher, which Claudette can't start. Install the native build of Claude Code, or choose **Choose claude…** and pick `claude.exe`.

## A version newer or older than the one Claudette was tested with

Claudette records the newest Claude Code version it was checked against, currently {{ site.claude_code.tested }}. Only the minimum, {{ site.claude_code.minimum }}, is required, so neither of these stops anything. They're notes:

- **Newer.** Settings → Claude Code says *Newer than the last tested version ({{ site.claude_code.tested }})*. Claude Code ships often, and Claudette is built to skip what it doesn't know. If something looks wrong after an update, see [Diagnostics and logs](diagnostics-and-logs.md).
- **Older than the last tested, but at the minimum or above.** Settings → Claude Code says *Older than the last tested version ({{ site.claude_code.tested }}); updating is recommended*. When the update badge offers a newer version, its card adds *Claudette was last tested with {{ site.claude_code.tested }}.*

## A tab that starts and then says Claude Code is too old

Claude Code can refuse to run itself if it's older than Anthropic allows. The tab says so, and you update Claude Code in Settings → Claude Code and restart the tab. See [A tab that won't start](a-tab-that-wont-start.md).

## See also

- [Claude Code's updates](../updates/claude-codes-updates.md)
- [What you need](../getting-started/what-you-need.md)
