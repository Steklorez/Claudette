---
title: Alerts
parent: Usage and limits
nav_order: 7
summary: "Claudette tells you when your session is getting full, so you don't have to watch the header."
---

# Alerts

Claudette tells you when your session is getting full, so you don't have to watch the header.

## When an alert comes

- **The session crosses a warning level.** One alert when it passes the warning level (75% by default), and one when it passes the alert level (90%). If it jumps past both, that's one alert. The title reads *Session usage passed 75%*, and the message says *You've used 76% of your session limit. It resets in 2h 14m.*
- **You're on pace to hit the limit.** When the projection first says you'll reach the limit before the session resets: *On pace to hit the session limit*, with the same line the header shows.
- **A limit resets.** When any limit's window starts over and its percentage drops: *Session limit reset*, *Weekly limit reset*, or a model's, with the new percentage.
- **Usage credits start paying.** When a limit is used up and your plan's usage credits start paying for Claude: *Using usage credits*, with the same line as [the notice under the header](the-usage-header.md#when-usage-credits-pay). Once each time they start.

Each alert comes once per window. The first reading after Claudette starts doesn't alert for levels you'd already crossed, or for usage credits already paying.

## Where they show

- **A line under the header**, with **Dismiss**. A threshold alert's line keeps up with the session meter, so the two never disagree. Usage credits have their notice instead, which stays until they stop paying.
- **An OS notification**, when Claudette isn't in front. Clicking it opens [the Usage window](the-usage-window.md).

[Notifications](../away/notifications.md) explains how notifications work on each system.

## Turning them off

Settings → Notifications has a switch, **Usage crosses a warning level, is on pace to run out, or resets**, that controls the OS notification. The line under the header still shows.

The levels themselves are **Warn at (% of session used)** and **Alert at (% of session used)** in Settings → Usage. They also set when the meters turn amber and red.

## Spend warnings

An API-key account can warn when its estimated spend passes an amount you set. Those use the same notification switch: see [Spend and its warnings](../accounts/spend-and-its-warnings.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Usage crosses a warning level, is on pace to run out, or resets** | On | Settings → Notifications |
| **Warn at (% of session used)** | 75 | Settings → Usage |
| **Alert at (% of session used)** | 90 | Settings → Usage |

## See also

- [When a limit stops a task](when-a-limit-stops-a-task.md) for what happens when you do hit one.
- [The usage header](the-usage-header.md) for the colors and the projection.
