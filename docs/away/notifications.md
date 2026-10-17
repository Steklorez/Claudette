---
title: Notifications
parent: Away from your desk
nav_order: 1
summary: "Your OS's notifications when a tab needs you and you aren't looking: a tab finished, a prompt is waiting, a limit, an update."
---

# Notifications

Claudette sends your operating system's own notifications when something needs you and you aren't looking at it: a tab finished, a prompt is waiting, a tab's Claude Code stopped. Each kind has its own switch.

On Windows they're toast notifications, on macOS they come from the Notification Center, and on Linux Claudette uses `notify-send`. Without `notify-send` there are no notifications on Linux, and a macOS build run with `dotnet run` has none either, since macOS needs the app itself. Settings → Notifications says so when notifications aren't available.

## The kinds

Every switch is on by default. They're under **Notify me when** in Settings → Notifications.

| Switch | When it comes | What it says |
|---|---|---|
| **A tab finishes its turn** | A tab's turn ended normally. A turn you stopped, or that ended with an error, doesn't count. | The first line of Claude's reply. After a turn that changed Claude's task list, it starts with where the list stands: *All 7 tasks done · Fixed the config and the tests pass.* or *4 of 7 tasks left · …* |
| **A tab needs permission or an answer** | A permission prompt, question, plan, or an MCP server's request for input is waiting. A Perforce login that needs you counts too. | What's waiting: *Allow this command? npm test*, *Claude has a question: Which database?*, *Claude has a plan for you to review.* |
| **A tab's Claude Code stops with an error** | A tab's Claude Code exited unexpectedly or couldn't start. | Why, such as *Claude Code stopped unexpectedly (exit code 3).* |
| **Usage crosses a warning level, is on pace to run out, or resets** | A [usage alert](../usage/alerts.md). | The alert's own text. |
| **Claude Code needs me to sign in** | Claude Code asks to sign in again. | *Your tabs are waiting until you sign in again.* |
| **A Claude Code update is ready** | An update to Claude Code has been installed or is waiting for you ([Claude Code's updates](../updates/claude-codes-updates.md)). | The version, and what to do. It comes once for each version. |
| **A project action finishes** | A long [project action](../project-tools/using-project-tools.md), such as a build, ended or failed. | *Build editor failed (exit code 6).* A job you stopped doesn't notify. |

A tab's notification carries the tab's name as its title.

**Check-ins** have their switch on another page: Settings → Check-ins → **Notify me when a check-in is sent**, off by default, which a tab can override in [Tab settings](../tabs/tab-settings.md). See [Check-ins on long turns](../composer/check-ins-on-long-turns.md).

## When they're skipped

- While Claudette is in front and the tab is on screen, selected or beside the one that is, a notification about that tab doesn't come. You're looking at it.
- Notifications that aren't about one tab, such as usage alerts, sign-in and updates, are skipped while Claudette is in front, because the header, the sign-in banner or the sign-in screen already shows them.
- A finished project action comes whichever tab is showing, and is skipped only while Claudette is in front, since the sidebar already shows the job.
- A kind whose switch is off never comes.

## One for each subject

A newer notification replaces an older one of the same kind for the same tab. A tab's notifications are taken away once you look at the tab, and a waiting prompt's goes as soon as it's answered. An update is announced once for each version, even across restarts.

## Clicking one

A click brings Claudette to the front and goes to what the notification is about.

- A tab's notification selects the tab, and opens its group if it was collapsed.
- A usage alert opens the [Usage window](../usage/the-usage-window.md).
- An update opens its dialog, and the sign-in notification opens the sign-in dialog.
- A project action's notification selects its tab and shows the run's log where the conversation is.

## Notifications and your phone

These are the notifications on this computer. Pushes to a phone are separate: they come from Claude Code, only when you've turned them on there, and only while Claudette isn't in front. See [The Claude app](the-claude-app.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **A tab finishes its turn** | On | Settings → Notifications |
| **A tab needs permission or an answer** | On | Settings → Notifications |
| **A tab's Claude Code stops with an error** | On | Settings → Notifications |
| **Usage crosses a warning level, is on pace to run out, or resets** | On | Settings → Notifications |
| **Claude Code needs me to sign in** | On | Settings → Notifications |
| **A Claude Code update is ready** | On | Settings → Notifications |
| **A project action finishes** | On | Settings → Notifications |
| **Notify me when a check-in is sent** | Off | Settings → Check-ins |

The badge and the icon's animation are in [The Dock and taskbar icon](the-dock-and-taskbar-icon.md). **Reset to defaults** on the page turns every one back on. Notification settings [sync](../history/settings-sync.md) between your machines.

## See also

- [The Dock and taskbar icon](the-dock-and-taskbar-icon.md) for the badge and flashing, which follow the same switches.
- [Notifications in the Reference](../reference/notifications.md) for the list at a glance.
- [Something missing on Linux](../troubleshooting/something-missing-on-linux.md).
