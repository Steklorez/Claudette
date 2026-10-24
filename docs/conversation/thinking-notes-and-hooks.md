---
title: Thinking, notes and hooks
parent: The conversation
nav_order: 3
summary: "Between your messages and Claude's replies, the conversation shows Claude's thinking, notices from Claude Code and Claudette, and the hooks that ran."
---

# Thinking, notes and hooks

Between your messages and Claude's replies, the conversation shows Claude's thinking, notices from Claude Code and Claudette, and the hooks that ran. They are quiet cards, so they stay out of the way of the answer.

## Thinking

A **Thinking** card holds what Claude thought before it replied. It starts collapsed, and says *Thinking…* while Claude is still at it. Click its line to open it.

- **Show thinking expanded** (Settings → Conversation, off by default) starts new thinking cards open.
- Right-click a thinking card for **Collapse all thinking** and **Expand all thinking**. They apply to every thinking card in this tab, the ones in subagents' groups too, and new ones start the same way from then on, whatever Settings says. The tab keeps the choice, and a copy of the tab (**Duplicate tab**, **Branch from here**) takes it.

[Find](finding-your-way.md#find) looks through thinking. [Exporting](exporting.md) leaves it out.

## Notes, warnings and errors

Claude Code and Claudette tell you things in one-line cards:

- A **Note** is quiet. Most notices are notes.
- A **Warning** is amber: a notice Claude Code marks as a warning, and a retry (below).
- An **Error** is red.

Claude Code's own notices include a model fallback (and the context window shrinking from 1M to 200K tokens when it falls back), a hook's message to you, which starts with the hook's name (*PostToolUse:Bash says: …*), and the reason a prompt-submit hook gave for blocking your message. Claudette adds notes of its own, such as the link to a tab's session in the Claude app.

### API retries

When Claude's API answers with an error and Claude Code tries again, one warning says so and updates in place instead of adding a row for each attempt: *The API returned an error (overloaded). Retrying (attempt 2 of 10) in 4s…*. When the service is having a bad day, [Claude is having problems](../troubleshooting/claude-is-having-problems.md) explains the dot in the header.

## Hooks

Hooks are commands Claude Code runs at set points, such as before a tool call. Yours and your project's run inside Claude Code, which tells Claudette about each run. How hooks are set up is in [Claude Code's hooks documentation](https://code.claude.com/docs/en/hooks).

- A hook that fails, or prints something while it runs, gets a **Hook** card with a row such as *PreToolUse hook (Bash) · failed (exit code 2)*. Click the row to see what it printed (up to 20,000 characters). A failed one opens by itself.
- The rest run quietly and show nothing.

Hook names and output are shown as text, whatever they contain.

### Showing every hook run

Turn on **Show every hook run** (Settings → Conversation, off by default) to give every run a row, from its start (*running…*) to how it ended (*done*, *failed* or *cancelled*).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Show thinking expanded** | Off | Settings → Conversation |
| **Show every hook run** | Off | Settings → Conversation |

## See also

- [Reading the conversation](reading-the-conversation.md) for what the cards' colors mean.
- [Exporting](exporting.md) for what an exported conversation keeps: failed hooks and errors, but not thinking or most notes.
