---
title: Sending and stopping
parent: The composer
nav_order: 1
summary: "The message box at the foot of a tab is where you talk to Claude."
---

# Sending and stopping

The message box at the foot of a tab is where you talk to Claude. You can send at any time, including while Claude is working, and stop it whenever you like.

{% include shot.html name="guide/composer/composer" alt="The composer: the message box with its bar of choices and counts under it, and the status line under that." %}

## Send a message

1. Click the message box and type. The box grows with your text, then scrolls.
2. Press `Enter`, or click the **Send** arrow.

`Shift+Enter` adds a new line. A message can be only images, only [quick suffixes](quick-suffixes.md) or only a large paste: **Send** works with any of them.

The empty box's placeholder says which keys send, which add a line and which stop Claude: *Message Claude…  (Enter to send, Shift+Enter for a new line, Esc to stop)*. What you've typed stays with the tab when you quit ([Earlier prompts, drafts and the stash](earlier-prompts-drafts-and-the-stash.md)).

## Change the key that sends

Settings → Keyboard → **Send with** has two choices:

- **Enter, with Shift+Enter for a new line**, the default.
- `Ctrl/Cmd+Enter`, with `Enter` for a new line.

With the second, `Ctrl/Cmd+Enter` on an empty box still answers a waiting prompt, as **Allow the waiting prompt** does ([Permission cards](../permissions/permission-cards.md)).

## Stop Claude

While Claude works, **Send** becomes **Stop**. Click it, or press `Esc`. **Stop Claude** is also in the [command palette](../tabs/the-command-palette.md).

Stopping interrupts the current turn. It doesn't close the tab or end the session, so you can send the next message straight away. It also takes back any messages that are still waiting (below): they return to the box instead of running once the turn ends.

If the `/` or `@` list is open, `Esc` closes the list and doesn't stop Claude.

## Messages sent while Claude works

Settings → General → **Messages sent while Claude works** chooses what happens to a message you send mid-turn.

**Send them into the turn: Claude reads each at its next step** is the default. Claude Code reads the message at its next tool call, or after the reply it's writing. Until it does, the message's card says *Queued: sent when Claude can take it*, with **Cancel**.

**Queue them: each is sent as the next turn, once this one ends** holds each message in Claudette until the turn ends, then sends them one per turn, oldest first. It's for lining up the next task without turning Claude from the one under way. A held message's card says *Queued: sent when this turn ends*, with two buttons:

- **Send now** sends it into the turn after all.
- **Cancel** takes it back.

**Cancel** puts what you wrote back in the box, with its images and large pastes, ahead of anything you've typed since, and a note says how many messages came back. If Claude Code has already taken a message, or can't give it back, the card stays as it is and a note says so.

Some things about queued messages:

- **Stop** takes back everything waiting, whichever setting is on. So does Claude Code exiting, because held messages can't go to a process that's gone.
- A turn that a usage limit stopped doesn't send the next held message, because the limit would stop it too. Held messages wait for the next turn to end, or for **Send now**. See [When a limit stops a task](../usage/when-a-limit-stops-a-task.md).
- A message sent while nothing is working goes at once.
- Claudette's own messages, [check-ins](check-ins-on-long-turns.md) and continuing after a limit, always go into the turn.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Send with** | **Enter, with Shift+Enter for a new line** | Settings → Keyboard |
| **Stop Claude** | `Esc` | Settings → Keyboard, in the list of shortcuts |
| **Messages sent while Claude works** | **Send them into the turn** | Settings → General |

## Shortcuts

| | Windows and Linux | macOS |
|---|---|---|
| Send | `Enter` | `Enter` |
| New line | `Shift+Enter` | `Shift+Enter` |
| Stop Claude | `Esc` | `Esc` |

## See also

- [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md), the line above the box while Claude works.
- [Permission modes](../permissions/permission-modes.md), picked from the bar.
