---
title: Codex's usage
parent: Codex
nav_order: 3
summary: "Codex's usage limits in the header, their alerts, and continuing in the other agent at a limit."
---

# Codex's usage

{% include unreleased.html %}

Codex has usage limits of its own, a five-hour window and a weekly one, separate from Claude's. The header shows whichever agent's the selected tab runs.

## The header follows the selected tab

Select a Codex tab, and the meters show Codex's windows: its session and week, the countdown to the session's reset, the sparkline and the projection. Select a Claude Code tab, and they're Claude's again. While tabs of both agents are open, the agent's icon stands before the meters, so it's clear whose they are. Codex sends its limits with each turn, so the meters fill in after a Codex tab's first turn. See [The usage header](../usage/the-usage-header.md).

The Usage window's session section names the agent while tabs of both are open, with the other agent's session and week on a line of their own under it. See [The Usage window](../usage/the-usage-window.md).

## Alerts

Codex's limits have [alerts](../usage/alerts.md) of their own, as Claude's do: passing 75% and 90% of the session, being on pace to hit the limit, and a limit resetting. Their notifications start **Codex:**, and while you use Codex, Claude's start **Claude:**. The line under the header is the shown agent's.

A Codex tab that hits its limit stops and waits for the reset, under the same rules as a Claude Code tab: **Continue when it resets**, or on its own if Settings → Tabs → **Continue tasks when a usage limit resets** is on. See [When a limit stops a task](../usage/when-a-limit-stops-a-task.md).

## Continuing in the other agent

Turn on Settings → Agents → All agents → **Offer to continue in the other agent**, and when a tab stops at its agent's limit, the bar under the conversation offers **Continue in Codex**, or **Continue in Claude Code** on a Codex tab, beside waiting for the reset.

It opens a tab of the other agent in the same folder, named after the first with the agent's name, such as *Grappling hook (Codex)*. Its first message is in the composer for you to trim and send: what you asked (the first message and the last two), the to-do list and how far it got, the files changed so far, and the last reply. The first tab stays, waiting for its reset.

## Settings

- Settings → Agents → All agents → **Offer to continue in the other agent**: off by default.
- The warning levels in Settings → Usage, and Settings → Tabs → **Continue tasks when a usage limit resets**, apply to both agents.

## See also

- [The usage header](../usage/the-usage-header.md), for Claude's limits.
- [Codex tabs](codex-tabs.md).
