---
title: Adding Codex
parent: Codex
nav_order: 1
summary: "Installing Codex, signing in to it, and what Deventry sets up for it."
---

# Adding Codex

{% include unreleased.html %}

Deventry runs the Codex you have installed, signed in with your own ChatGPT account. It can install Codex for you, and sign you in the way Codex's own `codex login` does.

{% include shot.html name="guide/codex/settings-codex" alt="Settings → Agents → Codex: Found, with Codex's version and where it is, and Look again; Path to codex; Account, signed in using ChatGPT, with Sign out; and New Codex tabs, with their Model and Mode." %}

## Installing Codex

Deventry looks for Codex as it starts: on your `PATH`, and where Codex's installer puts it.

1. Open Settings → Agents → **Codex**.
2. If **Found** says Codex wasn't found, click **Install Codex**. It runs Codex's own installer, the command its docs give (`irm https://chatgpt.com/codex/install.ps1 | iex` on Windows, `curl -fsSL https://chatgpt.com/codex/install.sh | sh` elsewhere), which installs it in your home folder with no admin rights, and shows what it prints.
3. Once it's done, Deventry looks again, and **Found** names the version and where it is.

The setup screen and the sign-in screen offer the same below Claude Code's: **Install Codex**, then **Sign in to Codex**. Claude Code is still what gets you past them.

If Codex is somewhere Deventry doesn't look, type its path in **Path to codex**, then click **Look again**.

## Signing in

Under **Account**, **Sign in** runs `codex login`, which opens your browser to sign in with your ChatGPT account, as it would in a terminal. **Sign out** runs `codex logout`. Deventry never signs in to Codex any other way, and keeps nothing of the sign-in: Codex keeps it, in its own folder.

## Codex's sandbox on Windows

Codex runs commands and edits in a sandbox. On Windows it has more than one kind, and none is set up until one is chosen, which would make every command and edit ask. So a Codex tab takes the first that's ready: the one your own Codex configuration chooses, then MXC, then the unelevated one, none of which needs setting up. If none is ready, the tab says so, and Codex asks before each command and edit; choose one in Codex's own configuration (`windows.sandbox`) to let it work on its own.

## In a Perforce workspace

In a Perforce workspace, files are read-only until they're checked out. With Settings → Perforce → **Check out before changes** on, Deventry checks files out before Codex changes them, through a hook that Codex runs before each edit. Codex runs a hook only once you've trusted it, so the first time a Codex tab opens in a workspace, Deventry asks **Let Codex check files out of Perforce?**, saying what the hook runs:

- **Trust Deventry's hook** writes its trust to Codex's own configuration, `config.toml`, as Codex's `/hooks` does. A new version of Deventry is a new hook, and asks again.
- **Not now** leaves Codex's edits without checking files out, which the tab says. It asks again the next time you open the tab.

## Settings

Settings → Agents → **Codex**:

- **Path to codex**: empty finds Codex by itself.
- **Model**: the model new Codex tabs start with; empty uses Codex's own default.
- **Mode**: what Codex may do without asking in a new tab: **Read only**, **Auto** (the default) or **Full access**, or **Plan**. Each tab can change it; see [Codex tabs](codex-tabs.md#modes).
- **Offer to continue in the other agent at a usage limit**: off by default; see [Codex's usage](codex-and-usage.md#continuing-in-the-other-agent).

## See also

- [Codex tabs](codex-tabs.md), for opening one and what's different in it.
- [Signing in](../getting-started/signing-in.md), for Claude Code's own sign-in.
