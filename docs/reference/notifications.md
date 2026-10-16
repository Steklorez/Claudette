---
title: Notifications
parent: Reference
nav_order: 3
summary: "Every kind of notification Claudette sends, the switch that turns it off, what it says and where a click goes."
---

# Notifications

Every kind of notification Claudette sends, the switch that turns it off, what it says and where a click goes. [Notifications](../away/notifications.md) explains how they work.

The switches are in Settings → Notifications, except check-ins, which are in Settings → Check-ins. All but check-ins are on by default. A notification about a tab has the tab's name as its title.

| Kind | Switch | What it says | A click goes to |
|---|---|---|---|
| A tab finishes its turn | **A tab finishes its turn** | The first line of Claude's reply. After a turn that changed Claude's task list, it starts with where the list stands: *"All 7 tasks done · Fixed the config and the tests pass."* or *"4 of 7 tasks left · …"*. Without a reply, *"Claude finished its turn."* A turn you stopped, or that ended with an error, doesn't notify. | The tab |
| A tab needs you | **A tab needs permission or an answer** | What's waiting: *"Allow this command? npm test"*, *"Claude has a question: Which database?"*, *"Claude has a plan for you to review."*, or an MCP server's request, such as *"github asks: …"*. For Perforce: *"Perforce needs your password to log in as matt @ ssl:perforce:1666."* or *"Perforce needs you to log in: run p4 login in a terminal, or log in with P4V."* | The tab |
| A tab's Claude Code stops | **A tab's Claude Code stops with an error** | Why: *"Claude Code stopped unexpectedly (exit code 3)."*, or why it couldn't start. | The tab |
| Usage | **Usage crosses a warning level, is on pace to run out, or resets** | *"Session usage passed 75%"* with how much you've used and when it resets, *"On pace to hit the session limit"* with the projection, and *"Session limit reset"* or *"Weekly limit reset"* with the new percentage. | The Usage window |
| Sign-in | **Claude Code needs me to sign in** | *"Claude Code needs you to sign in"*: *"Your tabs are waiting until you sign in again."* | The sign-in dialog |
| A Claude Code update | **A Claude Code update is ready** | That the update is ready, and that new tabs use it while open tabs keep their version until they close. Once per version. | The update dialog |
| A project action | **A project action finishes** | The job's result: *"Build editor failed (exit code 6)."* Only while Claudette isn't in front. A job you stopped doesn't notify. | The tab, with the Project page on that run's log |
| A check-in | **Notify me when a check-in is sent**, in Settings → Check-ins, off by default | *"Claude has been at this a while, so Claudette asked how it's going."* A tab can override the switch. | The tab |

Two more switches in Settings → Notifications are about the icon, not notifications: **Show the number of tabs needing input on the Dock or taskbar icon** and **Animate the Dock or taskbar icon while tabs are working or waiting**. See [The Dock and taskbar icon](../away/the-dock-and-taskbar-icon.md).

## Skipped notifications

- A notification about a tab is skipped when Claudette is in front and that tab is on screen, selected or beside the one you're in.
- App-wide ones (usage, sign-in, updates) are skipped while Claudette is in front, because the header, the banner or the sign-in screen already says it.
- A newer notification replaces an older one of the same kind for the same tab. A tab's notifications go away once you look at it, and a waiting prompt's goes once it's answered.
- A screen reader hears what a notification would say, shown or not. See [Accessibility](../settings/accessibility.md).

## API-key spend warnings

{% include unreleased.html %}

A spend warning for an [API-key account](../accounts/spend-and-its-warnings.md) is a usage notification, on the same switch: *"Studio: $5.20 today"* when its estimated spend passes the amount you set for the day, or *"Studio: $41.00 this month"* for the month.

## On each system

Windows shows toasts, and macOS uses its notification center. Linux uses `notify-send`, and has no notifications without it. A macOS build run with `dotnet run` has none, because notifications need the app bundle. See [Windows, macOS and Linux](windows-macos-and-linux.md).
