---
title: Codex tabs
parent: Codex
nav_order: 2
summary: "Opening a Codex tab, its modes, and what works as in a Claude Code tab and what doesn't."
---

# Codex tabs

{% include unreleased.html %}

A Codex tab is a tab like any other, running Codex instead of Claude Code. A tab keeps its agent: a conversation can't move from one to the other, though [Codex's usage](codex-and-usage.md#continuing-in-the-other-agent) covers handing the work over.

## Opening one

Once Codex is [added](adding-codex.md), right-click a folder group's header and choose **New Codex tab here**. **New Claude Code tab here** is beside it.

A folder remembers the agent its last new tab used: after a Codex tab, the group's next **New tab** opens Codex too, until you open a Claude Code tab there. Claude Code is the default for a folder that has neither.

While tabs of both agents are open, each tab's row shows its agent's icon before its name. With one agent, rows look as they always have.

## Modes

A Codex tab's mode picker, under the composer, has Codex's own modes:

- **Read only**: Codex reads, and asks before every command and edit.
- **Auto**: Codex works in the project's folder without asking, and asks for anything outside it, such as the network. The default.
- **Full access**: Codex does anything without asking. It asks you first, as **Bypass permissions** does in a Claude Code tab.
- **Plan**: Codex plans before it changes anything.

A permission card answers **Allow**, **Always allow**, which Codex keeps for the rest of the session, or **Deny**. See [Permission prompts](../permissions/index.md).

## What works as in a Claude Code tab

- **The conversation**: replies, reasoning, commands, edits, searches and web searches as cards, Changed files and each turn's footer, the to-do list pinned at the top, and stopping Codex or sending while it works.
- **Plan mode**: Codex's questions are the question card, and its plan the plan card, titled *Codex's plan*. **Approve, auto mode** switches to **Auto**, and **Approve, ask before edits** to **Read only**, then Codex carries the plan out; **Keep planning…** sends what you write, in **Plan** still. See [Plans, tasks and agents](../plans/index.md).
- **Subagents**: each subagent Codex starts is under its call, and on the agent map, where **Stop** stops it. Turning subagents off ([Subagents](../conversation/subagents.md)) starts Codex without them, and stops one it starts anyway.
- **Threads**: a Codex tab can be a thread, and either agent's tab its sub-thread. Codex sends work to them through a tool of Deventry's own, as Claude does through SendMessage. See [Threads](../threads/index.md).
- **Going back**: **Edit and resend** and **Branch from here** carry on as a copy of the conversation from just before the message. Codex keeps no copies of files, so Deventry puts them back from its own record of each turn's changes: only the turns the tab saw since it opened, and a file changed since is left as it is and named. See [Rewind and branch](../conversation/rewind-and-branch.md).
- **History and syncing**: Codex's past sessions are in History, each saying *Codex*, and open as Codex tabs. A Codex tab can [sync](../history/syncing-a-tab.md) through the session library, and another computer opens it in its own Codex.
- **Your project's notes**: what Deventry tells Claude Code about the project and its Perforce workspace, Codex is told too. Codex reads `AGENTS.md`; in a project with only a `CLAUDE.md`, a Codex tab reads that instead.

A restored Codex tab starts Codex as it's shown, rather than with your first message, since Codex reads its earlier conversation back only while it runs. If Codex no longer has the conversation, the tab says so and offers **Start a new session**.

## What only Claude Code has

These aren't offered on a Codex tab: connecting to the Claude app (Remote Control), usage credits, Ultracode, output styles, prompt suggestions, the prompt cache's countdown, Claude Docs, other Claude Code sessions in the composer's `@`, and accounts besides your own sign-in: a Codex tab runs under Codex's, so it has no account tag or **Account** menu.

## See also

- [Adding Codex](adding-codex.md), for installing and signing in.
- [Codex's usage](codex-and-usage.md), for its limits in the header.
