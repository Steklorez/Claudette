---
title: Spend and its warnings
parent: Accounts
nav_order: 5
summary: "An API key has no plan limits, so there's nothing for the usage meters to measure."
---

# Spend and its warnings

{% include unreleased.html %}

An API key has no plan limits, so there's nothing for the usage meters to measure. While a tab on an API-key account is selected, the header shows what that key is costing instead.

{% include shot.html name="accounts-standard" alt="A tab on the Studio API key: the header shows the key's estimated spend, Studio's tag on the orbit-game group and on a moved tab" %}

## The header's spend

It shows the account's name and color with *API key, estimated*, then **Today**, **This week** and **This month**. Hover over it for what's counted, and click it to open [the Usage window](../usage/the-usage-window.md). When you select a tab on the Default account again, the meters, the trendline and the chevron for the [detailed header](../usage/the-detailed-header.md) come back.

- **It's an estimate.** The amounts are Claude Code's own estimates at list price, added up over each turn, not your bill. Your actual bill is in the Claude Console.
- **Whose turns.** Turns from this machine's tabs, and, while [usage sharing](../usage/sharing-usage-across-machines.md) is on, other machines' tabs that sync to the session library under an account of the same name.
- **The week** starts on the first day of the week in your language and region settings, and the day and month start at local midnight.
- It updates after each turn, and again every 15 minutes, so a new day starts from nothing.

## Spend warnings

Each account can warn when its spend passes an amount. They're off until you set one.

1. Open Settings → Accounts.
2. On the account, set **Warn when today's estimated spend passes ($)**, **Warn when this month's estimated spend passes ($)**, or both. An empty box or 0 means no warning.

When the spend passes the amount, you get a notification such as *Studio: $12.40 today*, saying *The estimated spend on Studio's API key passed $10 today.* There's one a day and one a month at most, and Claudette remembers them, so a restart doesn't repeat one. The spend is the header's, so other machines' turns under the same name count. Each machine warns for itself.

The warnings are usage alerts, so the switch in Settings → Notifications, **Usage crosses a warning level, is on pace to run out, or resets**, turns them off too, and they're skipped while Claudette is in front: see [Alerts](../usage/alerts.md).

## In the Usage window

Once you have an account besides Default, the Usage window lists **Accounts** under **Projects**, over the same period: each account's tokens, estimated cost and turns. Only the Default account's turns count in its tab list, because a key's turns don't use your plan.

## What an API key doesn't have

- **Plan meters, charts and projections.** They're your Default account's.
- **The Claude app.** Its tabs can't connect to it: see [The Claude app](../away/the-claude-app.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Warn when today's estimated spend passes ($)** | Off | Settings → Accounts, on each account |
| **Warn when this month's estimated spend passes ($)** | Off | Settings → Accounts, on each account |
| **Usage crosses a warning level, is on pace to run out, or resets** | On | Settings → Notifications |

## See also

- [API-key accounts](api-key-accounts.md) for adding one.
- [The usage header](../usage/the-usage-header.md) for the plan's meters.
