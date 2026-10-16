---
title: The Usage window
parent: Usage and limits
nav_order: 3
summary: "The Usage window is the header's detail: a larger chart of the session, which tab and which project used the most, the week, and how past sessions and weeks went."
---

# The Usage window

The Usage window is the header's detail: a larger chart of the session, which tab and which project used the most, the week, and how past sessions and weeks went. It's a window of its own, so it can sit beside your work.

{% include shot.html name="guide/usage/usage-window" alt="The Usage window: the session chart with its projection, the tabs in this session window with their tokens, the Projects list for the week, and the week's chart" %}

## Opening it

Click the usage header, or its charts when [the detailed header](the-detailed-header.md) is on. A usage alert's notification opens it too. While a tab on an API-key account is selected, clicking the header's spend opens it.

## This session (5-hour window)

The session's percent and reset, the line that says where it's heading, and a chart of the window with the projection and faint dashed lines at the warning and alert levels. These are the header's own numbers: see [The usage header](the-usage-header.md).

## Tabs in this session window

The tokens each tab has used since the current 5-hour window started, heaviest first: each tab's name, a bar for its share, its tokens, its estimated cost and its turns. It answers which session is burning your limit fastest.

- Costs are Claude Code's estimates at list price, not your bill. On a plan, the limits are what count.
- A tab you've closed keeps the last name it had, even after a restart. If Claudette doesn't know one, it's *A closed tab*.
- With [usage sharing](sharing-usage-across-machines.md) on, tabs from your other machines show too, as *Fix the loader · Laptop*.

## Projects

The tokens, estimated cost and turns for each project, across every tab that worked in it, open or closed, in any of its folders (a worktree, another clone), heaviest first, with each project's share of the total. Pick the period with the box at the right, **Period**:

- **This session window**
- **This week**, the default
- **The last 30 days**, as far back as **Keep usage history** goes

A row is named for its folder. When two share a name, the parent folder is added, and hovering shows the full path. Turns from before Claudette noted each turn's folder are one row, **Earlier turns**.

With usage sharing on, a project adds up the tabs other machines sync to the [session library](../history/the-session-library.md), matched by the repository and the folder inside it, whatever each machine calls the folder. Hover over a row for what each machine spent, such as *This machine: $3.10 · 12 turns*.

## Accounts

Once you have an API-key account besides Default, **Accounts** appears under **Projects**, over the same period: the tokens, estimated cost and turns each account has used, with Default's turns as one row. Hover over a row for the split by machine.

Only turns under your Default account count in the tab list and in the header's charts, because a key's turns don't use your plan. See [Spend and its warnings](../accounts/spend-and-its-warnings.md).

## This week

The weekly meters, each with its percent and reset, and a chart of the week's usage up to its reset.

## Past sessions and past weeks

Two lists of how earlier windows went, newest first, as far back as the [usage history](usage-history.md) goes: *Session ending Mon Oct 12, 3:45 PM*, with a bar and *peaked at 82%*. Each shows the highest usage that window reached. The lists show the latest 30 sessions and 12 weeks, and a button under each, such as **Show 30 more (45 left)**, adds the next page.

A note at the foot says how long the history is kept.

## See also

- [The usage header](the-usage-header.md) and [The detailed header](the-detailed-header.md) for the same numbers on the main window.
- [A tab's tokens](a-tabs-tokens.md) for one tab's breakdown.
