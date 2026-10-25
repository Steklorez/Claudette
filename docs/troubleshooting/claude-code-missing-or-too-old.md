---
title: Claude Code missing or too old
parent: Troubleshooting
nav_order: 1
summary: "Claudette runs your installed Claude Code, so without one it can't start a tab."
---

# Claude Code missing or too old

Claudette runs your installed Claude Code, so without one it can't start a tab. When Claude Code is missing, too old or won't answer, Claudette shows a setup screen before anything else. [The first launch](../getting-started/the-first-launch.md) describes the screen; this page is for fixing what it says.

## Claude Code isn't installed

Claudette found no `claude` on your `PATH` or where Claude Code installs to, or no file at the path you gave it. The screen's text says which.

Choose **Install Claude Code**. It runs Claude Code's own installer and shows what it prints, and when it's done Claudette carries on by itself. [Installing Claude Code from the setup screen](../getting-started/the-first-launch.md#installing-claude-code-from-the-setup-screen) has the command it runs.

Or install Claude Code yourself:

1. Install Claude Code with [Claude Code's setup instructions](https://code.claude.com/docs/en/setup). **Install instructions** on the screen opens them.
2. Choose **Check again**.

If Claude Code is installed but Claudette can't find it:

- Use **Choose claude…** to pick the `claude` program. Claudette remembers it in Settings → Claude Code → **Path to claude**.
- On macOS and Linux, a Claudette started from the Dock or a launcher doesn't see the `PATH` your terminal does. It reads your login shell's, so check that your shell's startup files add the folder `claude` is in: see [Claude can't find a program](claude-cant-find-a-program.md).
- If the screen says a path *doesn't exist*, the path in Settings points at a file that's gone. Use **Choose claude…** to pick the right file.

## Install Claude Code didn't work

The screen shows the installer's output, and its last lines under it.

- **Nothing happens for a while.** The installer prints nothing while it downloads Claude Code. Wait a few minutes before deciding it's stuck.
- **It failed.** The installer couldn't reach `downloads.claude.ai`, through a proxy or a firewall for example, or Claude Code isn't available where you are. On macOS and Linux it needs `curl`. Run the command the screen shows in a terminal to see more, and see Claude Code's [Troubleshoot installation](https://code.claude.com/docs/en/troubleshoot-install).
- **It finished, but Claudette still can't use Claude Code.** The screen says what Claudette found. Restart Claudette, or use **Choose claude…** to pick `claude` in `~/.local/bin` (`claude.exe` in `.local\bin` in your user folder on Windows).

## You have the Claude desktop app

The Claude desktop app isn't Claude Code. It keeps a copy of Claude Code for its own use, which it replaces whenever it updates, and Claudette doesn't run it. When Claude Code isn't installed and the desktop app is, the setup screen says so: choose **Install Claude Code**, which installs it beside the desktop app.

If **Path to claude**, or a file you chose with **Choose claude…**, is one of the desktop app's, the screen says *That's the Claude desktop app, not Claude Code*, or *That's the copy of Claude Code the Claude desktop app keeps for its own use*, with the path. On Windows the app is `C:\Program Files\WindowsApps\Claude_…\app\claude.exe`, which is easy to find when you search for `claude.exe`. Choose **Install Claude Code**, or use **Choose claude…** to pick Claude Code's own `claude`.

## Claude Code needs an update

The screen says which version Claudette found. Claudette needs {{ site.claude_code.minimum }} or later.

- Choose **Update now**. It runs the update for the way Claude Code was installed and shows the output. When it works, Claudette checks again by itself.
- If **Update now** isn't offered, or fails, update Claude Code yourself and choose **Check again**. [Claude Code's updates](../updates/claude-codes-updates.md) has the command for each way of installing it.

## Claude Code didn't respond

Claudette runs `claude --version` to read the version. The screen says what went wrong: the command failed, ran too long, or printed something that isn't a version. Run `claude --version` in a terminal. If that fails too, Claude Code itself needs repairing: reinstall it. If it works, choose **Check again**, or **Choose claude…** if a different `claude` should be used.

## This Claude Code install isn't supported yet

On Windows, Claude Code installed with npm can leave only a `claude.cmd` launcher, which Claudette can't start. Choose **Install Claude Code** to install the native build of Claude Code, or use **Choose claude…** to pick `claude.exe`.

## A version newer or older than the one Claudette was tested with

Claudette records the newest Claude Code version it was checked against, currently {{ site.claude_code.tested }}. Only the minimum, {{ site.claude_code.minimum }}, is required, so neither of these stops anything. They're notes:

- **Newer.** Settings → Claude Code says *Newer than the last tested version ({{ site.claude_code.tested }})*. Claude Code ships often, and Claudette is built to skip what it doesn't know. If something looks wrong after an update, see [Diagnostics and logs](diagnostics-and-logs.md).
- **Older than the last tested, but at the minimum or above.** Settings → Claude Code says *Older than the last tested version ({{ site.claude_code.tested }}); updating is recommended*. When the update badge offers a newer version, its card adds *Claudette was last tested with {{ site.claude_code.tested }}.*

## A tab that starts and then says Claude Code is too old

Claude Code can refuse to run itself if it's older than Anthropic allows. The tab says so, and you update Claude Code in Settings → Claude Code and restart the tab. See [A tab that won't start](a-tab-that-wont-start.md).

## See also

- [Claude Code's updates](../updates/claude-codes-updates.md)
- [What you need](../getting-started/what-you-need.md)
