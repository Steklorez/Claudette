---
title: Prompt suggestions
parent: The composer
nav_order: 8
summary: "After Claude replies, Claude Code can guess what you'll send next."
---

# Prompt suggestions

After Claude replies, Claude Code can guess what you'll send next. Claudette shows the guess as gray text in the empty message box, as the terminal does ([Claude Code's prompt suggestions](https://code.claude.com/docs/en/interactive-mode#prompt-suggestions)).

## Turn them on

Prompt suggestions are off by default. Each one is a short extra model call on the conversation, mostly reads from the prompt cache, and it counts toward your usage.

1. Open Settings → Claude Code.
2. Turn on **Suggest my next prompt**.

It applies to tabs started after the change. If you turn it off, no suggestion shows, but a session that's running keeps making them until it restarts.

## Use a suggestion

When a suggestion is ready, the empty box shows it in place of the usual placeholder, such as *run the tests  (Tab to use)*.

- Press `Tab` or `→` to put it in the box, with the caret at its end. Send it with `Enter`, or change it first.
- `Enter` on the empty box doesn't send it.
- Typing anything dismisses it for good. Emptying the box again doesn't bring it back.

A suggestion also goes when you send a message, when any turn starts (a check-in, the Claude app, a thread), on `/clear`, and when Claude Code exits. One that arrives after you've started typing or sent something isn't shown. Suggestions aren't saved with the tab.

## When there's none

Claude Code makes no suggestion for some turns: a conversation's first, one that ended in an error, one in Plan mode, one near the usage limit, and one when the prompt cache is cold.

Claude Code has its own switches too. **Prompt suggestions** in its `/config`, or `promptSuggestionEnabled: false` in its settings, turns them off even with **Suggest my next prompt** on. So does the environment variable `CLAUDE_CODE_ENABLE_PROMPT_SUGGESTION=false`.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Suggest my next prompt** | Off | Settings → Claude Code |

## Shortcuts

| | Windows and Linux | macOS |
|---|---|---|
| Take the suggestion | `Tab` or `→` | `Tab` or `→` |

## See also

- [Earlier prompts, drafts and the stash](earlier-prompts-drafts-and-the-stash.md), for bringing back prompts you've already sent.
- [Command line and environment](../reference/command-line-and-environment.md), for the variables that pass through to Claude Code.
