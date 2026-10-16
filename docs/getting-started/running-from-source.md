---
title: Running from source
parent: Getting started
nav_order: 8
---

# Running from source

You can build and run Claudette from a checkout of its [repository](https://github.com/dotBunny/Claudette). It's the way to run it on Linux, where there is no package, and the way to try changes. It works on Windows and macOS too.

You need Claude Code installed ([What you need](what-you-need.md)) and the .NET SDK version the repository's `global.json` names. If `dotnet` says it can't find that SDK, install the one `global.json` asks for.

## Running it

From the checkout's root:

```sh
dotnet run --project src/Claudette.App
```

This uses your real account, so messages cost usage as they do in any tab. To open a tab in a folder at startup:

```sh
dotnet run --project src/Claudette.App -- --folder <path>
```

## Running from a copy

A source build copies its build output to a `builds` folder in Claudette's data folder and runs from there. That lets you rebuild while it runs: on Windows a running program's files are locked, and elsewhere replacing them can crash it. `dotnet run` returns as soon as the copy has started.

To run in place instead, set `CLAUDETTE_RUN_IN_PLACE=1`, or attach a debugger. Claudette counts as a source build when its program is in a `bin` folder below a checkout that holds `Claudette.slnx`.

A source build never checks for releases. Settings → General says so.

## When a new build is ready

Claudette checks the build output every two seconds. Once a build's files have stopped changing, **New build ready** appears at the foot of the sidebar. A failed build writes nothing, so it's never offered. Choose the badge to see when the build was made:

- **Restart now** restarts into it.
- **Restart when idle** waits until no tab is starting, working or waiting on you. **Don't restart** cancels the wait.
- **Restart into new builds by itself when no tab is working** does that every time. It's remembered on this machine.

The restart keeps every tab, in order, with its session, name, settings and the message you were typing, the selected tab, and the window's size and place. A tab that was working carries on: Claude Code re-runs the turn by itself, once, and the conversation says *Carrying on with the turn the restart cut off.* Processes the tabs started are stopped, as when Claudette closes.

If the new build doesn't start, the old one takes its tabs back and the badge's window says why, with the last lines the new build wrote. The build stays offered, to try again once it's fixed, but isn't restarted into by itself again.

Stop Claudette by closing its window, not by killing the process. Closing interrupts running turns and stops each tab's `claude` and what it started. A killed Claudette leaves them running.

## Which build is this

The foot of the Settings sidebar shows the version and, for a source build, the commit it was built from, such as `Claudette 0.3.0 · 842169b`. Hover it to see which configuration it was built in. Click it to copy the versions for a bug report: see [Reporting an issue](../troubleshooting/reporting-an-issue.md).

## Keeping it apart from your own Claudette

Two environment variables keep a development copy out of your real setup:

- `CLAUDETTE_HOME` is a folder for Claudette's settings, saved tabs and data. A copy with its own `CLAUDETTE_HOME` counts as a separate Claudette, so it runs beside your installed one.
- `CLAUDE_CONFIG_DIR` points Claude Code at another configuration folder.

See [Command line and environment](../reference/command-line-and-environment.md).

## Try it without an account

`tools/Claudette.Demo` makes a demo: three game projects (Unreal in C++, Unity in C#, and Godot), seven tabs including a thread with two sub-threads, past sessions for History, and a usage history, with a stand-in for Claude Code. It needs no account and spends no tokens.

```sh
dotnet run --project tools/Claudette.Demo -- <empty folder>
```

Give it an empty folder, or one an earlier run made. It writes the demo there and prints the environment to start Claudette with:

```
CLAUDETTE_HOME=<the folder>/home
CLAUDE_CONFIG_DIR=<the folder>/claude-config
FAKE_CLAUDE_USAGE=<the folder>/usage.json
CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
```

Set those, then run Claudette as above. In PowerShell, set each one like this:

```powershell
$env:CLAUDETTE_HOME = "C:\demo\home"
```

The conversations show the projects' paths. `--projects <folder>` puts the projects somewhere else, so you can choose a path that says nothing about you.

## See also

- [Installing](installing.md), for the packages.
- [Windows, macOS and Linux](../reference/windows-macos-and-linux.md).
