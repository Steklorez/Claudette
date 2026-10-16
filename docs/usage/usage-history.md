---
title: Usage history
parent: Usage and limits
nav_order: 9
---

# Usage history

Claudette keeps your usage on this machine, in a small database, so the trendline, the charts and the per-tab numbers survive a restart.

## What's kept

- **Plan usage readings**: the session and weekly percentages (and each model's, when its meter is on) with their reset times, saved when a value changes and at most once a minute. They feed the trendline and projection, the weekly charts, and the header after a restart.
- **Each turn's token counts**: when it ran, which tab and project, the model, input, output, cache write and cache read tokens, an estimated cost, and the account it ran under. They feed each tab's token chart, the busiest tabs, and the Projects list in [the Usage window](the-usage-window.md).
- **Tab names**: the last name each tab had, so its rows still have a name once the tab is closed.

It never keeps your prompts, replies or code. Those stay in Claude Code's own transcripts. The file stays small even with **Forever**: roughly tens of megabytes a year of heavy use.

## How long it's kept

Settings → Usage → **Keep usage history** sets it: **1 day**, **1 week**, **1 month** (the default), **1 year** or **Forever**. Older records are deleted when Claudette starts and once a day. With **1 day** the weekly chart only covers the last day. The header's meters and projection aren't affected, because they only need the current window and the latest weekly value.

## Clearing it

1. Open Settings → Usage and click **Clear usage history…**.
2. Optionally tick **Also reset per-tab token totals**. Otherwise each tab keeps its own running totals, which are saved with the tab: see [A tab's tokens](a-tabs-tokens.md).
3. Click **Clear**, or **Cancel**.

It deletes every stored reading, turn record and tab name. The header's meters fill in again at the next update from Claude Code. It only clears this machine's history: usage you've shared through the session library stays, and so does what your other machines imported. To clear those too, see [Sharing usage across machines](sharing-usage-across-machines.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Keep usage history** | 1 month | Settings → Usage |
| **Clear usage history…** | | Settings → Usage |

**Reset to defaults** on that page leaves **Keep usage history** as it is.

## See also

- [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md) for where the file is.
- [Sharing usage across machines](sharing-usage-across-machines.md).
