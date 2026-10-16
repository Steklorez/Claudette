---
title: Check-ins on long turns
parent: The composer
nav_order: 9
summary: "If a turn runs for a long time, Claudette can ask Claude how it's going, so a tab doesn't sit stuck and burn usage without anyone noticing."
---

# Check-ins on long turns

If a turn runs for a long time, Claudette can ask Claude how it's going, so a tab doesn't sit stuck and burn usage without anyone noticing.

## When a check-in is sent

Either of two things starts a check-in. Each can be changed or turned off:

- **Run time.** The turn has been running for longer than a set time, 15 minutes by default.
- **Quiet time.** The turn has produced no new output for a set time, 5 minutes by default.

## The countdown

When a check-in is due, a bar over the message box counts down 30 seconds first: *Checking in with Claude in 30 s.* Hover it to see the message that will be sent.

- **Send now** sends it straight away.
- The ✕ (**Don't check in**) skips it. The timers start again, so the next check-in is a whole interval away, as if Claude had just said something.

The bar goes away by itself if the turn no longer calls for a check-in: Claude said something after a quiet spell, a permission prompt or question came up, or the turn ended. Once a check-in is sent it can't be taken back.

## What happens

Claudette sends a message into the session, as if you'd typed it while Claude was working. The default is:

> Everything OK? Give me a one or two sentence status update: what you're doing, and whether you're stuck or waiting on something.

In the conversation it shows as your message with an **Automatic check-in** label, so it's clear you didn't type it. Claude's reply shows as usual, and the turn carries on.

Claude only sees the message between steps. If it's running one long command, such as a long test run, the check-in arrives when that command finishes. If Claude is in the middle of a long text-only reply, the check-in runs as the next turn.

## Possibly stuck

If two check-ins in a row get no reply, Claudette stops sending them for that turn. A bar over the box says *Claude hasn't answered two check-ins. It may be stuck.*, with **Stop**, and the tab's row in the sidebar says *Possibly stuck*. What to do about it is in [A tab that seems stuck](../troubleshooting/a-tab-that-seems-stuck.md).

## Limits

- At most one check-in per interval. The timers restart after Claude replies.
- No check-in while the tab is waiting on a permission prompt or question. That's waiting on you, not on Claude.
- Check-ins use a small amount of usage, counted in the tab's tokens like any other message.

## Settings

Settings → Check-ins:

| Setting | Default |
|---|---|
| **Check in on long turns** | On |
| **After the turn has run (minutes)** | 15 |
| **After no output for (minutes)** | 5 |
| **Message** | The message above |
| **Notify me when a check-in is sent** | Off |

Set either time to 0 to turn that trigger off. **Notify me when a check-in is sent** also sends a system notification ([Notifications](../away/notifications.md)).

A tab can have its own: **Tab settings…** → **Use different check-in settings for this tab**, with **On**, **Run time (min)**, **Quiet time (min)**, **Message** and the notification switch. See [Tab settings](../tabs/tab-settings.md).

## See also

- [Sending and stopping](sending-and-stopping.md): a check-in always goes into the turn, whichever way **Messages sent while Claude works** is set.
- [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md), which shows how long the turn has run.
