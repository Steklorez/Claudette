---
title: Permission cards
parent: Permissions and prompts
nav_order: 1
summary: "When Claude Code needs your permission to use a tool, such as running a command or editing a file, the tab shows a card in the conversation and waits for you."
---

# Permission cards

When Claude Code needs your permission to use a tool, such as running a command or editing a file, the tab shows a card in the conversation and waits for you. The tab's row in the [sidebar](../tabs/the-sidebar.md) says *Needs your input*, and if Claudette isn't in front, or the tab isn't the one you're looking at, you also get a [notification](../away/notifications.md).

{% include shot.html name="guide/permissions/permission-card" alt="A permission card waiting for an answer." %}

## What a card shows

- The question, such as *Allow this command?*, *Allow editing StarfallCharacter.cpp?* or *Allow fetching this page?*
- The call's input: the command, the file or address, or a preview of an edit's changes as a diff.
- Claude's description of the call, and why Claude Code is asking, when it says.
- A warning when a command asks to run outside the sandbox, which would give it the file and network access the sandbox keeps from it.
- Who is asking, when it isn't the main Claude: *Asked by the Explore subagent: Find the auth code*. The [agent map](../plans/the-agent-map.md) highlights that subagent too.

Once you answer, the card stays in the conversation and says what happened, such as *Allowed* or *Denied*.

## Answering a card

- **Allow** lets this one call go ahead.
- **Always allow…** shows the rule it would save, for example `Bash(npm test:*)`, in a box you can edit to make it broader or narrower. Several rules go in one box, separated by commas.
  - **Save rule and allow** allows the call and saves the rule to the folder's `.claude/settings.local.json`, which is Claude Code's personal, uncommitted project settings. Terminal sessions in that folder use the rule too, and later matching calls don't ask.
  - **Allow for this session only** allows the call and the rule until the session ends, and saves nothing. It's also in the menu on **Always allow…**'s arrow.
  - **Cancel** goes back to the first buttons.
- **Allow all edits this session** appears when Claude Code suggests switching mode instead of saving a rule, as it does for a file edit. It allows the call and switches the tab to Accept edits mode ([Permission modes](permission-modes.md)).
- **Deny** refuses the call.
- **Deny with a message…** opens a box, *Tell Claude what to do instead (optional)*. **Deny** sends it, **Cancel** goes back.

**Always allow…** isn't offered when Claude Code says the rule would grant more than this one request does. For the rules themselves, see [Claude Code's permissions docs](https://code.claude.com/docs/en/permissions).

## Answering from the keyboard

`Ctrl/Cmd+Enter` allows and `Ctrl/Cmd+Backspace` denies the oldest prompt waiting in the tab. Neither works while you're typing in one of a prompt's own fields, where `Ctrl/Cmd+Backspace` still deletes a word. A request Claude Code marks as one to refuse by default can't be allowed from the keyboard.

`Ctrl/Cmd+J` goes to the next tab that's waiting for you.

## Several prompts waiting

Subagents working side by side can leave prompts all through a long conversation. While more than one waits in a tab, each says where it stands, oldest first: *Prompt 2 of 5*, with **‹** and **›** to go to the one before or after. Prompt 1 is the one the keyboard answers. Answering a prompt, or Claude Code withdrawing it, numbers the rest again.

## When a prompt is answered elsewhere

Claude Code can withdraw a prompt it no longer needs. Its card closes and says *No longer needed*, or *Answered in the Claude app* when you answered it on a tab connected to [the Claude app](../away/the-claude-app.md).

## Finding a waiting prompt

{% include unreleased.html %}

A mark beside the conversation's scroll bar shows where each waiting prompt is, and a click goes there. [Finding your way](../conversation/finding-your-way.md) explains the marks.

## What never asks

Claudette only shows the prompts Claude Code actually asks for. Claude Code's own allow and deny rules, from its settings files, still apply: a call a rule allows never reaches you, and read-only commands such as `sleep` never ask at all. When a rule or the mode denies a call without asking, the conversation shows a note saying so.

## Settings

- Settings → Notifications → **A tab needs permission or an answer** (on by default) sends the notification.
- Settings → Keyboard → **Allow the waiting prompt** and **Deny the waiting prompt** change the shortcuts.

## See also

- [Permission modes](permission-modes.md) decide how much Claude Code asks about.
- [Questions](questions.md) and [Approving a plan](approving-a-plan.md) are prompts with cards of their own.
- [Sub-threads waiting on you](../threads/sub-threads-waiting-on-you.md): answering a sub-thread's permission card from its thread.
