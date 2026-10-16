---
title: Sending work and getting reports
parent: Threads
nav_order: 3
---

# Sending work and getting reports

You give the work to the thread's Claude in the thread's own tab, as you would to any tab: "Have Art page redo the sprites and Audio rebalance the mix, and tell me when both are done." Its Claude sends each sub-thread a message, and when they've all finished, it gets their results back.

## Sending

The thread's Claude uses Claude Code's own `SendMessage` tool with a sub-thread's name. Claudette sees the call first and delivers the message into that sub-thread's tab itself.

By default the thread asks you first. A card in the strip over the thread's composer says *Send this to Art page?*, shows the message, and has two buttons:

- **Send** delivers it.
- **Don't send** doesn't. Claude is told so.

The tab says it needs your input while a message waits, with a [notification](../away/notifications.md). If you don't answer within 10 minutes, the message isn't sent and Claude is told you didn't say.

To skip the question, turn off **Ask before sending to sub-threads**, in the tab's **Thread** submenu or the command palette. It's a switch on each thread, saved with the tab, and it's on by default.

### How a message arrives

- If the sub-thread is idle, the message goes in at once. If it's busy, the message is held until its turn ends ([Sending and stopping](../composer/sending-and-stopping.md) explains held messages). So a thread never interrupts work you started in a sub-thread.
- In the sub-thread, the message's card is labeled *From the thread ‹name›*. Its Claude is told who it's from and that its final reply goes back by itself.
- Claude gets the call's answer as the tool's result, saying the message went and that results come back by themselves. Claude Code marks that kind of result as an error, but the tool row in the thread shows it as done, not failed.
- A call that only asks to be told when a sub-thread is idle is stopped, because the report does that.
- Everything else goes through as Claude Code would send it: a name that isn't one of the sub-threads', any tab that isn't a thread, and the messages subagents send each other.

## Getting the report

Claudette waits on a sub-thread from the moment it delivers it a message until the turns that message started have ended. When none of the thread's sub-threads is waited on any more, the thread gets one message, *From the sub-threads*, with each one's outcome and last reply. An outcome is one of:

- Finished.
- Stopped by you before it finished.
- Ended with an error.
- Its tab was closed before it finished.
- It was taken off the thread before it finished.

A reply is cut at 20,000 characters; the rest is in the sub-thread's tab. The report goes into the thread's turn if it's working, as a check-in does, and otherwise starts its next turn. So a thread that sends work to three sub-threads takes one more turn, not three, and the sub-threads spend nothing writing reports.

## Settings

There's nothing in Settings for this. **Ask before sending to sub-threads** is per thread, as above.

## See also

- [Sub-threads waiting on you](sub-threads-waiting-on-you.md): answering their prompts.
- [Check-ins on long turns](../composer/check-ins-on-long-turns.md): the other message that goes into a working turn.
