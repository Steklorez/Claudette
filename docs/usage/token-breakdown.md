---
title: A tab's token breakdown
parent: Usage and limits
nav_order: 5
summary: "What a tab's tokens went on, turn by turn and call by call, in tokens and cost, with what cost more than it needed to, and what the next message will cost."
---

# A tab's token breakdown

{% include unreleased.html %}

A tab's token breakdown says what its tokens went on: what kind they were, turn by turn and call by call, by model, by agent and by tool, and what cost more than it needed to, such as a turn sent after the prompt cache had expired. It's a window of its own, one for each tab.

{% include shot.html name="guide/usage/token-breakdown" alt="The token breakdown of the Grappling hook tab: tiles with its tokens, estimated cost, share of the session limit, cache, model calls and tool calls; the tokens and cost of each kind as bars; a column for each turn with the context after it; and its findings, models, agents and tools" %}

## Opening it

- Right-click a tab and choose **Token breakdown…**, under **Session**.
- **Show token breakdown** in the command palette opens the selected tab's.
- **Breakdown ›** on a turn's card in the conversation opens it on that turn.
- **Open breakdown** in the composer bar's token count popup, or in the [Tokens tool window](#the-tokens-tool-window), where a click on one of the latest turns opens it on that turn.
- **Breakdown ›** beside **Tokens** on the tab's info card (the **ⓘ** at the end of the line under the message box).

Opening it again brings its window to the front, on the turn you asked for.

## What the tokens were

Claude Code counts four kinds of token, and the breakdown shows each in its own color:

- **Cache read**: the conversation so far, read from the [prompt cache](the-prompt-cache.md) at a tenth of the input price.
- **Cache write**: what's new since the last call, written to the cache at more than the input price.
- **Fresh input**: neither. Claude Code caches nearly everything, so there's little of it.
- **Output**: what Claude wrote, thinking included, at five times the input price.

Counted in tokens, cache reads are nearly all of a tab. Counted in cost, which is closer to what counts against your plan's limits, they're much less, and output, a sliver of the tokens, is a good share. So the window shows two bars, one in tokens and one in cost, with each kind's numbers and share under them.

**Tokens** and **Cost**, at the top of the window, switch the chart and the bars between the two. Deventry remembers your choice on this computer.

## The tiles

- **Tokens** and **Estimated cost**: the cost is Claude Code's estimate at list price, not your bill.
- **Session limit**: about how much of the current 5-hour window this tab used, marked as an estimate. Each time the session meter rose, the rise is shared between the tabs working then by their cost; its tooltip says how much rose while no tab here was working, from another computer or claude.ai. A tab on an API-key account doesn't use the plan.
- **From the cache**: the share of what Claude read that came from the prompt cache.
- **Model calls**, the main conversation's and its subagents', and **Tool calls**.

## Turn by turn

A column for each turn, its kinds stacked, with its number and the time it ended. A mark over it says what happened:

- a dot, in the caution color: the turn was sent after the prompt cache had expired, so its first call wrote the whole conversation to the cache again;
- a chevron: Claude Code compacted the conversation first;
- a diamond: a subagent worked in it.

Under them, the context after each turn. Hover over a column for its numbers, and click it to find its turn in the table.

## The turns

A row for each turn: its prompt, from the conversation, when it ended and how long it ran, its model calls (*4 +9* is four of the main conversation's and nine by subagents), its tokens and cost, how much came from the cache, and how full the context was after it. Click a turn to see its calls in order, with what each tool gave Claude between them: *Read · its result added 4.7k, read again by 13 later calls*.

The calls add up to the turn. Some calls Claude Code makes itself and doesn't show, such as compacting the conversation, naming it, or reading the pages WebFetch fetched; each turn has a row for those, so nothing is left out.

## Findings

Plain words about what cost more than it needed to, the costliest first, each with **Go to turn**:

- a turn sent after the prompt cache had expired, and what writing the conversation again cost;
- a subagent that cost a tenth of the tab or more, and what a cheaper one of the same kind cost, if one ran;
- the tool result read again the most, when that's a large part of the tab's cache reads: a big file or log stays in the conversation until it's compacted or cleared, and every later call reads it again;
- each compaction, how far it shrank the context, and what a call cost either side of it;
- Claude Code's own calls, when they cost a twentieth of the tab or more.

## By model, agent and tool

- **By model**: each model's calls, tokens and cost.
- **By agent**: the main conversation, each subagent with its kind, turn and the size of its report, and Claude Code's own calls.
- **By tool**: each tool's calls, what their results added to the conversation, and what later calls read again because of them.

## The period and Export

The period at the top of the window is **This session window**, **Today** or **All time**. A turn keeps its number in any of them.

**Export…** saves the turns and their calls as a CSV file, with each turn's prompt.

## The Tokens tool window

The **Tokens** tool window keeps the parts of the breakdown that help while you work beside the conversation: what's happening now, and what comes next. Its icon joins the [tool windows](../tabs/tool-windows.md)' strip once the tab has used tokens, and **Show tokens** in the command palette shows it.

{% include shot.html name="guide/usage/tokens-window" alt="The Tokens tool window of the Grappling hook tab: the context, This tab's tiles with its tokens, cost, share of the session limit and cache, the tokens and cost bars, its latest turns newest first with their prompts and costs, and its findings" %}

- **Your next message**, between turns: what its first model call costs if you send it before the [prompt cache](the-prompt-cache.md) expires, and after, with why. Before, Claude reads the conversation from the cache; after, it writes all of it to the cache again, which costs many times more. Once the cache has expired, it says what sending now costs. It's the first call only: how many more a turn takes is up to Claude.
- **Turn 15 · running**, while Claude works: the turn's tokens and cost so far, each model call as it comes in, with what each tool added between them (*↳ Read · +8.1k*), a subagent's calls as one row, and *Claude is choosing its next step…* after a tool's result.
- **Context**: how full the context is, with the end Claude Code compacts it at, when it says.
- **This tab**: its tokens, cost, share of the session limit and how much came from the cache, and its tokens and cost as bars by kind, the turn running included.
- **Latest turns**: the last six, newest first, each with its prompt, a bar of its cost by kind and the cost, a dot when it was sent after the cache had expired. Click one to open the breakdown on it.
- **Findings**: the costliest two, each with **Go to turn ›**, and **Open breakdown** for the rest.

## On each turn's card

The **Turn** card at the end of each turn says its number and what it cost, with a thin bar of its kinds of token under it. When the turn was sent after the prompt cache had expired, a warning says what reading the conversation again cost. **Breakdown ›** opens the breakdown on that turn. See [The end of a turn](../conversation/the-end-of-a-turn.md).

## What's kept

Deventry keeps each call's numbers and its tools' names in [the usage history](usage-history.md), for as long as **Keep usage history** says, and shares them with a synced tab's turns when [usage sharing](sharing-usage-across-machines.md) is on. The prompts aren't kept there: the window reads them from the conversation. Turns from before this version of Deventry have their totals but not their calls.

## See also

- [A tab's tokens](a-tabs-tokens.md), the count in the composer bar.
- [The Usage window](the-usage-window.md), for every tab's use of the session and the week.
- [The prompt cache](the-prompt-cache.md), and why an expired cache costs more.
- [Context and compacting](context-and-compacting.md).
