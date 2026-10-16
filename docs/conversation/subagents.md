---
title: Subagents
parent: The conversation
nav_order: 4
---

# Subagents

Claude can hand part of its work to a subagent: a helper that runs inside the same session with its own instructions and its own tool calls, and hands a report back. Claude Code decides when to start one ([Claude Code's subagents documentation](https://code.claude.com/docs/en/sub-agents) explains them). This page is about how they look in the conversation. The [agent map](../plans/the-agent-map.md) shows all of a tab's subagents at once.

## A subagent's card

A subagent is a card of its own, in the quiet neutral color. Its line shows a status dot (running, done or failed), the subagent's type (*Explore*, *general-purpose* and so on), a short description of its task and the time. Under it is its result line: the first line of its report once it has finished, or how it ended.

Click the line to open the card. Inside is the start of the prompt Claude gave the subagent, then everything that subagent did: its own **Tool calls** cards, its replies and its thinking, as the main conversation shows them. A subagent can start subagents of its own, which nest inside it. The agent map's details show the whole prompt and the whole report, each with **Copy**.

## Subagents that run in the background

A subagent Claude doesn't wait for runs in the background. Its group in the conversation says it is running until the subagent finishes, and the **Agents** button in the composer bar counts it. The tab's row in the sidebar keeps showing that work is going on after the turn is over.

To stop a subagent, use the **■** on its row in [the agent map](../plans/the-agent-map.md). The running tasks chip leaves subagents out ([The working line and running tasks](the-working-line-and-running-tasks.md)).

## Questions from a subagent

When a subagent needs permission, the card that asks says who is asking: *Asked by the Explore subagent: Find the auth code*. You answer it like any other ([Permission cards](../permissions/permission-cards.md)).

## In a restored tab

A restored tab shows each subagent's finished work, tool calls included, but no live status or running clock. A subagent with no recorded result reads *Didn't finish*.

## Finding things in a subagent

[Find](finding-your-way.md#find) looks inside subagents' groups, and a match there opens the group. Text you select inside a subagent can be added to the [scratch pad](the-scratch-pad.md), like any other.

## Settings

**Summarize subagents' progress** (Settings → Claude Code, off by default) is explained with [the agent map](../plans/the-agent-map.md).

## See also

- [The agent map](../plans/the-agent-map.md) for the tree of every agent and stopping one.
- [Threads](../threads/index.md) for work spread over several tabs, which is different: each sub-thread is a whole tab you can watch and steer.
