---
title: A tab that seems stuck
parent: Troubleshooting
nav_order: 4
summary: "A turn can run a long time without being stuck: a test run or a build may take many minutes."
---

# A tab that seems stuck

A turn can run a long time without being stuck: a test run or a build may take many minutes. A turn that's stuck keeps using your plan's usage and gets nowhere, so Claudette watches for it.

## What Claudette does

When a turn has run for a long time, or has produced no output for a while, Claudette asks Claude how it's going.

1. A bar over the message box counts down: *Checking in with Claude in 30s.* **Send now** sends the question straight away. The ✕ (**Don't check in**) skips it, and the timers start again.
2. Claude's reply to the check-in appears in the conversation, and the turn carries on.
3. If two check-ins in a row get no reply, Claudette stops asking for that turn. The tab's row says *Possibly stuck*, and a bar over the message box says *Claude hasn't answered two check-ins. It may be stuck.* with **Stop**.

By default a check-in starts after 15 minutes of running or 5 minutes without output. [Check-ins on long turns](../composer/check-ins-on-long-turns.md) has the whole story.

## What to try

1. **Look before you stop it.** Claude sees a check-in only between steps. If a single command is running, such as a long test run, the check-in is delivered when that command finishes. Open the **Processes** page of the side panel (turned on in Settings → Tabs → **Show the process monitor**) to see whether the command is still busy, and read the conversation's last tool call.
2. **Choose Stop**, or press `Esc`. It interrupts the turn but doesn't close the session. Then tell Claude what to do next, or what it was stuck on.
3. **Restart the tab** if its Claude Code has exited: **Restart** in the status line resumes the same session. See [A tab that won't start](a-tab-that-wont-start.md).

## Things that look stuck and aren't

- **Waiting on you.** A tab waiting on a permission prompt, a question or a plan isn't counted: it shows *needs input*, and `Ctrl/Cmd+J` goes to the next tab waiting for you.
- **A usage limit.** A tab stopped at a limit waits for the reset, and carries on by itself if you've left that on. See [When a limit stops a task](../usage/when-a-limit-stops-a-task.md).
- **Claude is having trouble.** Retries and overloaded errors can come from Claude's side. Check the dot in the header: see [Claude is having problems](claude-is-having-problems.md).
- **A message you sent while Claude was working.** It waits for the next step, or for the turn to end if you chose to queue them.

## Settings

Settings → Tabs → Check-ins on long turns:

- **Check in on long turns**, on by default.
- **After the turn has run (minutes)** and **After no output for (minutes)**. Set a time to 0 to turn that trigger off.
- **Message**, what Claudette asks.
- **Notify me when a check-in is sent**, off by default.

A single tab can override them in its **Tab settings…**.

## See also

- [Sending and stopping](../composer/sending-and-stopping.md)
- [Processes](../tabs/processes.md)
