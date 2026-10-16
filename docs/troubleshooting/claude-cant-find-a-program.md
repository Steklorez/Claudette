---
title: Claude can't find a program
parent: Troubleshooting
nav_order: 5
summary: "On macOS and Linux, you may see Claude fail to run node, dotnet, brew or another program in a Claudette tab, while the same command works in a terminal with claude."
---

# Claude can't find a program

On macOS and Linux, you may see Claude fail to run `node`, `dotnet`, `brew` or another program in a Claudette tab, while the same command works in a terminal with `claude`.

An app started from the Dock, Finder or a desktop launcher gets a minimal environment. On macOS its `PATH` is just `/usr/bin:/bin:/usr/sbin:/sbin`. Homebrew, nvm, pyenv and the like are set up in your shell's startup files, such as `~/.zprofile` or `~/.bashrc`, which a launcher doesn't read. So Claudette reads your login shell's environment, as VS Code does, and gives it to every `claude` it starts and to the other programs it runs for you.

On Windows apps get your full environment, so this page doesn't apply there.

## Check that it's on

Settings → Claude Code → **Use my login shell's environment** (macOS and Linux only). It's on by default. It applies to programs started after you change it.

## See what Claudette did

Settings → Advanced → **Diagnostics** has a **Login shell environment** row. It says whether Claudette used the shell's environment, which shell, how long it took, and the names of the variables it added or changed. It never shows their values, which can be secrets. When it didn't use it, the row says why:

- **Turned off**, in Settings.
- **Started from a terminal.** Then Claudette's environment already came from a shell, so it doesn't read another. Claudette counts as started from a terminal when `TERM` is set or it has a controlling terminal.
- **The shell timed out or failed.** Claudette gives the shell 10 seconds and carries on with its own environment if that isn't enough.
- **The shell isn't supported.**

## Fixes

- **Put the setup where a login shell reads it.** Claudette runs your shell (`$SHELL`, or `zsh` on macOS and `bash` on Linux) as a login, interactive shell. Variables and `PATH` entries your startup files set there are picked up. Anything the files print is ignored.
- **It reads the shell once per run.** After you change a startup file, restart Claudette.
- **Supported shells:** bash, zsh, sh, dash, ksh, mksh, yash, fish, tcsh and csh. Nushell, xonsh and PowerShell aren't run, and Diagnostics says so. If that's your login shell, start Claudette from a terminal.
- **A slow shell.** While Claudette reads the environment it sets `CLAUDETTE_RESOLVING_ENVIRONMENT=1`. A startup file can check that variable and skip slow or interactive setup, such as a prompt theme or a version manager's long start.
- **Start Claudette from a terminal.** Then it uses that terminal's environment, and `dotnet run` builds do too.

Claude Code's own Bash tool reads your shell's startup file for aliases and functions, but it takes environment variables from the environment Claudette gave it, so the fix above is the one that matters.

## What gets it

Every `claude` Claudette starts (tabs and its helper), and the programs it runs for you: git, `p4` and P4V, diff tools, and project tools' builds, editors and actions. `claude` and the diff tool presets are looked for on the login shell's `PATH` too. Claudette's own small helpers keep its own environment.

## Settings

- Settings → Claude Code → **Use my login shell's environment**: on by default, macOS and Linux only.

## See also

- [Claude Code missing or too old](claude-code-missing-or-too-old.md), when it's `claude` itself that isn't found.
- [Command line and environment](../reference/command-line-and-environment.md)
