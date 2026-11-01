---
title: Command line and environment
parent: Reference
nav_order: 4
summary: "How to start Deventry from a terminal, the environment variables Deventry reads, and what Deventry passes to the programs it starts."
---

# Command line and environment

How to start Deventry from a terminal, the environment variables Deventry reads, and what Deventry passes to the programs it starts.

## The command line

| Option | What it does |
|---|---|
| `--folder <path>` | Opens a tab in that folder on startup. A relative path is from where you ran the command, also when a Deventry that's already running takes it. The jump list, **File → Open Recent** and the Dock menu use it too. |
| `--login` | How the login entry starts Deventry: minimized, without taking focus. A Deventry that's already running ignores it. You don't need to type it. See [Starting at login](../away/starting-at-login.md). |

Deventry has two other options, `--restore` and `--source-build`, which it passes itself when it restarts into an update or a new build. Don't use them.

**One Deventry at a time.** Starting Deventry while it's running hands the options to the running one, which comes to the front, and opens a tab if you gave a folder. A copy started with its own `DEVENTRY_HOME` counts as a separate Deventry.

To run from a checkout: `dotnet run --project src/Deventry.App -- --folder <path>`. See [Running from source](../getting-started/running-from-source.md).

## Deventry's variables

| Variable | What it does |
|---|---|
| `DEVENTRY_HOME` | A folder where Deventry keeps all its settings and data, instead of your profile's. It's for trying things without touching your real tabs. A Deventry started with it also leaves the login entry alone, and **Start Deventry when I log in** is disabled. |
| `DEVENTRY_RUN_IN_PLACE` | Set to `1`, a source build runs from its build folder instead of from a copy. A debugger attached does the same. See [Running from source](../getting-started/running-from-source.md). |
| `DEVENTRY_UNFOCUSED` | Set to `1`, Deventry's windows never take the focus from the window you're in, and on Windows they stay behind your windows. The screenshot scripts in `tools/Deventry.Demo` use it. |
| `DEVENTRY_AUTOMATION_PIPE` | The name of a pipe Deventry takes commands on: the screenshot scripts in `tools/Deventry.Demo` click, type and press keys in it this way, without the mouse or the keyboard. Only your own programs can connect. |

## Claude Code's variables

Deventry starts every `claude` with your environment (on macOS and Linux with your login shell's variables merged in, [below](#what-a-login-shell-sees)), after removing the variables Claude Code sets for the programs it starts, such as `CLAUDECODE` and `CLAUDE_CODE_CHILD_SESSION`. Left in, they'd make a `claude` started from a terminal inside a Claude Code session behave as a child session. Deventry removes exactly those, and nothing by prefix: your own `CLAUDE_CONFIG_DIR`, `CLAUDE_CODE_USE_BEDROCK` and the rest pass through to `claude` as your configuration. See [How Deventry uses Claude Code](../getting-started/how-deventry-uses-claude-code.md).

[Claude Code's own list of variables](https://code.claude.com/docs/en/env-vars) says what each one does. These are the ones that change what Deventry shows:

| Variable | What Deventry does |
|---|---|
| `CLAUDE_CONFIG_DIR` | Passes through, so the tab's Claude Code uses that configuration folder. |
| `ANTHROPIC_BASE_URL` | Passes through. The Claude app isn't available when Claude is reached at another address, and connecting says why. See [The Claude app](../away/the-claude-app.md). |
| `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` | Pass through to the Default account's tabs. A key or token also means the Claude app isn't available. |
| `CLAUDE_CODE_USE_BEDROCK` and the other cloud provider switches | Pass through. Claude through a cloud provider can't use the Claude app. |
| `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` | Passes through. Claude Code then makes no title for the session, so the tab keeps its folder's name. |
| `DISABLE_UPDATES`, `DISABLE_AUTOUPDATER` | Pass through. They turn Claude Code's own updates off. See [Claude Code's updates](../updates/claude-codes-updates.md). |
| `CLAUDE_CODE_MAX_SUBAGENT_SPAWN_DEPTH` | Passes through. It limits how deeply subagents nest (three layers by default), so the agent map can be shallower. See [The agent map](../plans/the-agent-map.md). |

Deventry also sets some variables itself on every `claude` it starts:

| Variable | Why |
|---|---|
| `CLAUDE_CLIENT_PRESENCE_FILE` | Names a file that exists only while Deventry's window is in front. Claude Code skips pushes to your phone while it exists, so they reach you only when you're away from Deventry. See [The Claude app](../away/the-claude-app.md). |
| `CLAUDE_CODE_STARTUP_FAILURE_RESULTS` | Makes a start that Claude Code refuses say why, so the tab can tell you in words. See [A tab that won't start](../troubleshooting/a-tab-that-wont-start.md). |
| `CLAUDE_CODE_ENABLE_TODO_TOOLS` | Set while Settings → Claude Code → **Give Claude a task list** is on, so newer models make a task list too. |

### Keys and accounts

A tab on an [API-key account](../accounts/api-key-accounts.md) starts without `ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN`, which would otherwise win over the account's key. The key itself never goes in the environment: Claude Code gets it from a small helper Deventry runs.

### The prompt cache

To say how long the prompt cache lasts, Deventry reads `CLAUDE_CODE_PROMPT_CACHE_TTL`, `ENABLE_PROMPT_CACHING_1H`, `FORCE_PROMPT_CACHING_5M`, `DISABLE_PROMPT_CACHING` and the per-model `DISABLE_PROMPT_CACHING_…` variables, both from the environment and from the `env` of Claude Code's settings files. See [The prompt cache](../usage/the-prompt-cache.md).

## What project actions get

Actions that run in each new worktree get two more variables, so a command can copy a file from the main checkout:

| Variable | Value |
|---|---|
| `DEVENTRY_WORKTREE` | The new worktree's folder. |
| `DEVENTRY_MAIN_CHECKOUT` | The main checkout's folder. |

See [Actions and links](../project-tools/actions-and-links.md#setting-up-each-new-worktree).

## What a login shell sees

An app started from the Dock, Finder or a desktop launcher gets a minimal environment: on macOS its `PATH` is only the system's. Homebrew, nvm and the like are usually set in your shell's startup files. So on macOS and Linux, Deventry reads your login shell's environment once, in the background, and gives it to `claude` and to the programs it starts for you: git, `p4`, diff tools and project tools. It's the same `PATH` as a terminal's. Windows apps get your full environment, so Windows never needs it.

- **When.** Settings → Claude Code → **Use my login shell's environment**, on by default, on macOS and Linux, when Deventry wasn't started from a terminal. Started from a terminal, it already has that shell's environment.
- **Which shell.** Your `$SHELL`, or `zsh` on macOS and `bash` on Linux, run as an interactive login shell. bash, zsh, sh, dash, ksh, mksh, yash, fish, tcsh and csh work. Others, such as nushell, xonsh and PowerShell, aren't run, and Diagnostics says so.
- **If it fails.** It gets 10 seconds. On a timeout or a failure Deventry carries on with its own environment.
- **`DEVENTRY_RESOLVING_ENVIRONMENT`.** It's set to `1` while the shell runs, so a startup file can skip slow or interactive setup:

  ```sh
  if [ -z "$DEVENTRY_RESOLVING_ENVIRONMENT" ]; then
    # slow or interactive setup, such as a prompt theme
  fi
  ```

- **What it doesn't change.** Deventry's own `DEVENTRY_*` variables always come from Deventry's environment, and Claude Code's session variables are never taken from the shell.
- **Checking it.** Settings → Advanced → **Diagnostics** says whether it was used, which shell, how long it took and the names of the variables it added or changed, never their values.

If Claude can't find a program, see [Claude can't find a program](../troubleshooting/claude-cant-find-a-program.md).
