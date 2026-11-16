---
title: The Usage window
parent: Usage and limits
nav_order: 3
summary: "The Usage window is the header's detail: a larger chart of the session, where the tokens went and which tab, project, model and tool used the most, the week, and how past sessions and weeks went."
---

# The Usage window

The Usage window is the header's detail: a larger chart of the session, where the tokens went and which tab, project, model and tool used the most, the week, and how past sessions and weeks went. It's a window of its own, so it can sit beside your work.

{% include shot.html name="guide/usage/usage-window" alt="The Usage window: the session chart with its projection, Tokens over This session window, where the tokens went as bars of tokens and cost with each kind's share, and the tabs, the costliest first, each with a bar of its cost" %}

## Opening it

Click the usage header, or its charts when [the detailed header](the-detailed-header.md) is on. A usage alert's notification opens it too. While a tab on an API-key account is selected, clicking the header's spend opens it.

## This session (5-hour window)

The session's percent and reset, the line that says where it's heading, and a chart of the window with the projection and faint dashed lines at the warning and alert levels. These are the header's own numbers: see [The usage header](the-usage-header.md).

## Tokens over

{% include unreleased.html %}

Every section from here to **Tools** covers the period you pick beside **Tokens over**:

- **This session window**, since the current 5-hour window started, where it starts
- **This week**, the weekly limit's week
- **The last 30 days**, as far back as **Keep usage history** goes

Deventry remembers your choice on this computer.

## Where the tokens went

{% include unreleased.html %}

A line with the period's tokens, estimated cost and turns, then two bars of the four kinds of token, one in tokens and one in cost, and each kind's numbers and share: cache read, cache write, fresh input and output. Counted in tokens, cache reads are nearly everything; counted in cost, which is closer to what uses your limits, output and cache writes are a good share. See [What the tokens were](token-breakdown.md#what-the-tokens-were).

When turns were sent after the [prompt cache](the-prompt-cache.md) had expired, a line in the caution color says how many and what reading their conversations again cost: *4 turns began after the cache expired: $2.73 to read their conversations again.*

## Tabs

{% include unreleased.html %}

Each tab's tokens over the period, the costliest first: its name, its project, a bar of its cost by kind, its tokens, estimated cost and turns, and how many of its turns found the cache expired. It answers which session is burning your limit fastest. Click a tab, or its **›**, for its [token breakdown](token-breakdown.md).

- Costs are Claude Code's estimates at list price, not your bill. On a plan, the limits are what count.
- A tab you've closed says *closed* and keeps the last name it had, even after a restart. If Deventry doesn't know one, it's *A closed tab*. Its breakdown still opens, from the usage history: its turns go by their numbers, as its conversation isn't open.
- A tab on an [API-key account](../accounts/index.md) says the account's name.
- With [usage sharing](sharing-usage-across-machines.md) on, tabs from your other machines show too, with the machine's name, such as *starfall · Laptop*.

This list used to be **Tabs in this session window**, of your Default account's tabs only. [The detailed header](the-detailed-header.md)'s busiest tabs still are.

## Projects

The tokens, estimated cost and turns for each project over the period, across every tab that worked in it, open or closed, in any of its folders (a worktree, another clone), heaviest first, with each project's share of the total.

A row is named for its folder. When two share a name, the parent folder is added, and hovering shows the full path. Turns from before Deventry noted each turn's folder are one row, **Earlier turns**.

With usage sharing on, a project adds up the tabs other machines sync to the [session library](../history/the-session-library.md), matched by the repository and the folder inside it, whatever each machine calls the folder. Hover over a row for what each machine spent, such as *This machine: $3.10 · 12 turns*.

## Accounts

Once you have an API-key account besides Default, **Accounts** appears under **Projects**, over the same period: the tokens, estimated cost and turns each account has used, with Default's turns as one row. Hover over a row for the split by machine.

Only turns under your Default account count in the header's charts and busiest tabs, because a key's turns don't use your plan. See [Spend and its warnings](../accounts/spend-and-its-warnings.md).

## Models

{% include unreleased.html %}

Each model's calls over the period, the costliest first, with its cache reads, cache writes, output and estimated cost.

## Tools

{% include unreleased.html %}

Each tool's calls over the period, with what their results added to the conversation, how much later calls read again because of them, and what that cost. A result stays in the conversation until it's compacted or cleared, so a big file or log is read again by every call after it. The tools read again the most come first.

## This week

The weekly meters, each with its percent and reset, and a chart of the week's usage up to its reset.

## Past sessions and past weeks

Two lists of how earlier windows went, newest first, as far back as the [usage history](usage-history.md) goes: *Session ending Mon Oct 12, 3:45 PM*, with a bar and *peaked at 82%*. Each shows the highest usage that window reached. The lists show the latest 30 sessions and 12 weeks, and a button under each, such as **Show 30 more (45 left)**, adds the next page.

A note at the foot says how long the history is kept.

## See also

- [The usage header](the-usage-header.md) and [The detailed header](the-detailed-header.md) for the same numbers on the main window.
- [A tab's tokens](a-tabs-tokens.md) and [A tab's token breakdown](token-breakdown.md) for one tab.
