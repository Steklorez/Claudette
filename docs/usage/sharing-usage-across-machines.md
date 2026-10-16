---
title: Sharing usage across machines
parent: Usage and limits
nav_order: 11
summary: "Your plan's limits belong to your account, so every machine signed in to it sees the same usage."
---

# Sharing usage across machines

Your plan's limits belong to your account, so every machine signed in to it sees the same usage. But each machine only charts the readings it took itself and only counts the turns it ran, so a machine that sat idle has gaps. Sharing fills them in through the [session library](../history/the-session-library.md).

## Turning it on

Settings → Usage → **Share usage with my other machines**. It's off by default. Turn it on on each machine that should take part.

## What's shared

- **Plan readings** from the last 8 days: the session, weekly and model percentages, their reset times and when each was taken.
- **Turns of tabs that sync to the library** ([Syncing a tab](../history/syncing-a-tab.md)): each turn's time, model, tokens, estimated cost, project and tab name, and the account it ran under. Turns from before turns could be shared, and turns of a tab that doesn't sync, stay on their machine.

Your prompts, replies and code aren't part of it. The usage database itself never goes into the library: each machine writes its own readings as a small file there, and each synced session carries its turns.

## What you see

- **Other machines' readings** appear in the header's trendline and projection, the detailed header and the Usage window. Only machines signed in to the same plan account count, so your email isn't written to the library. When the newest reading is newer than this machine's, the header shows it.
- **Projects** in [the Usage window](the-usage-window.md) add up every machine's turns in a project, matched by the repository and the folder inside it. Hover over a row for what each machine spent.
- **Tabs in this session window** and the busiest tabs list another machine's tabs as *Fix the loader · Laptop*.
- **Accounts** counts another machine's API-key turns with this machine's account of the same name. See [Spend and its warnings](../accounts/spend-and-its-warnings.md).

Machines look for each other's usage at launch and once a minute. If another machine runs a newer Claudette whose shared files this one can't read yet, it leaves them out until you update, and says so in its log.

## Turning it off

Turning it off stops this machine writing and reading. The files already in the library stay, as a session's copy does when its tab stops syncing.

## Clearing usage on every machine

{% include unreleased.html %}

**Clear usage history…** clears this machine's history only. **Clear usage history on every machine…**, under **Share usage with my other machines**, clears the shared usage too. It's available while sharing is on.

1. Click **Clear usage history on every machine…**.
2. Optionally tick **Also reset per-tab token totals on every machine**.
3. Click **Clear everywhere**, or **Cancel**.

This machine's history is cleared, and so is the usage shared in the library. Every machine that shares clears its own history up to that moment the next time it checks: within a minute if it's running, or when it starts. What a machine recorded after that moment stays, and nothing cleared comes back. The sessions themselves aren't touched.

The setting tells you how it went: *Usage history cleared here and in the session library. Your other machines clear theirs the next time they check, within a minute if they're running.* If the library can't be reached, only this machine's history is cleared, and it says so.

A machine that isn't sharing isn't cleared. If you turn sharing on there later, it then clears its history up to the moment of the clear.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Share usage with my other machines** | Off | Settings → Usage |
| **Clear usage history on every machine…** | | Settings → Usage |

## See also

- [Usage history](usage-history.md) for what each machine keeps.
- [What leaves this computer](../privacy/what-leaves-this-computer.md).
