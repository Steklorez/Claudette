---
title: A tab that won't start
parent: Troubleshooting
nav_order: 3
summary: "A tab that can't start, or whose Claude Code stops, shows an error icon in the sidebar."
---

# A tab that won't start

A tab that can't start, or whose Claude Code stops, shows an error icon in the sidebar. Its row and its [info card](../tabs/the-tab-info-card.md) say what went wrong: the last line Claude Code wrote to its error output, or why it couldn't start. If you aren't looking at the tab, you also get a notification, such as *Claude Code stopped unexpectedly (exit code 3).*

## Restart

When a tab's Claude Code has stopped, a **Restart** button appears in the status line under the message box. It starts Claude Code again and resumes the same session, so the conversation carries on. If the reason is still there, the tab fails again with the same message.

## The reasons a tab gives

Claude Code tells Claudette why it refused to start, and the tab puts it in words with what to do, followed by what Claude Code said. The common ones:

- **Your organization's settings.** They don't allow the sign-in or the provider this session is set up for, require a sign-in you don't have, or couldn't be loaded or were refused. Check the network or proxy, sign in with another account, or ask an administrator. A tab says, for example, *You're signed in to an organization your organization's settings don't allow. Sign in with another account.*
- **An API key conflict.** Your organization requires its own sign-in, but an API key, auth token or `apiKeyHelper` is set. Remove it, then restart the tab.
- **A proxy setting that isn't a URL.** Fix or unset it, then restart the tab.
- **Git for Windows missing.** Claude Code needs Git Bash to run commands on Windows. Install it from [git-scm.com](https://git-scm.com/downloads/win), then restart the tab.
- **Claude Code too old.** Update it in Settings → Claude Code, then restart the tab. See [Claude Code's updates](../updates/claude-codes-updates.md).
- **Bypass permissions mode as root.** Claude Code won't use that mode as root. Choose another permission mode in the tab's **Tab settings…**, then restart.
- **A conversation held by a background session.** It's running as a background session in Claude Code. Stop it there, or open a copy from [History](../history/history.md).
- **A worktree Claude Code won't resume the session in.** Restarting the tab may work.
- **Claude Code's temporary folder.** It's unsafe or couldn't be created.

A reason Claudette doesn't know yet shows Claude Code's own words.

## Signed out

If Claude Code isn't signed in, a banner offers **Sign in**, and the tab waits. See [Signing in](signing-in.md).

## A folder that's gone

If the tab's folder was deleted or moved, the tab says *The folder … no longer exists. If it moved, choose where it is now and the session carries on there.* It offers:

- **Choose folder…**, for a folder that moved, or another clone of the same project. The tab moves to that folder's group and the session carries on there.
- **Unpin and close**, or **Close tab** for a tab that isn't pinned.

The same happens when Claude Code can't use the folder, for instance because it can't be read.

## A session that's gone

Claude Code deletes transcripts after 30 days by default. A restored tab whose session is gone from this computer and from the [session library](../history/the-session-library.md) says *The earlier conversation couldn't be found here or in the session library (Claude Code may have cleaned it up). You can start a new session in this folder.* It doesn't start one by itself:

- **Start a new session** starts one in the same folder, keeping the tab's name, pinned state, settings and suffixes.
- **Unpin and close** (or **Close tab**) closes it.

## The tab is waiting for you

A tab waiting on the trust question, a permission prompt or a sign-in isn't broken. It shows as needing input, and `Ctrl/Cmd+J` goes to the next tab that is waiting. See [Folder trust](../permissions/folder-trust.md).

## See also

- [A tab that seems stuck](a-tab-that-seems-stuck.md), for a tab that started and then went quiet.
- [Closing and restoring](../tabs/closing-and-restoring.md)
- [Diagnostics and logs](diagnostics-and-logs.md)
