---
title: What threads are
parent: Threads
nav_order: 1
---

# What threads are

A **thread** is a tab whose Claude can hand work to other tabs in the same folder group. Those tabs are its **sub-threads**. The thread's Claude sends each sub-thread a message, the sub-threads do the work in their own tabs, and when they've finished the thread gets their results back in one message. Each sub-thread is a whole tab: you can watch what it's doing, steer it, and answer its prompts.

{% include shot.html name="tasks-standard" alt="A thread selected, with its sub-threads indented under it in the sidebar, SendMessage calls in the conversation, and its tasks and plan on the Tasks page." %}

Threads are different from [subagents](../conversation/subagents.md), which run inside one session where you can't step into them. A sub-thread is a tab like any other.

## How it goes

1. You [make a tab a thread](making-a-thread.md) and give it sub-threads: new tabs in its folder, or existing ones from the same group.
2. You ask the thread's Claude to split up a task. It sends each sub-thread a message with Claude Code's own `SendMessage` tool, which is what Claude Code uses to message another session ([Claude Code's tools reference](https://code.claude.com/docs/en/tools-reference) lists it).
3. Unless you've turned that off, the thread asks you before each message goes ([Sending work and getting reports](sending-work-and-getting-reports.md)).
4. The sub-threads work. Their permission prompts, questions and plans stay yours: you answer them in their tabs, or from a strip over the thread's composer ([Sub-threads waiting on you](sub-threads-waiting-on-you.md)).
5. When none of the sub-threads is still working on what it was sent, the thread gets one message with each one's outcome and last reply, and carries on.

## What Claude is told

Setting a thread up costs no turn. The next message you send in the thread carries a note naming its sub-threads and how they work, and again whenever its sub-threads change. That message's card shows *Told Claude about its sub-threads* under it, with the note as its tip. After **Stop being a thread**, the next message tells Claude it's no longer one.

A sub-thread's Claude is told who a message is from, *From the thread ‹name›*, and that its final reply goes back on its own, so it answers there instead of messaging back.

## Prompts stay yours

Claudette never answers a sub-thread's permission prompts, questions or plans for you, and the thread's Claude never hears of them. They wait for you.

## What threads don't do

- Threads don't nest: a sub-thread can't be a thread.
- Sub-threads come from the thread's own folder group only.
- Threads aren't synced through the [session library](../history/the-session-library.md).
- The thread's Claude can't make sub-threads or answer their prompts. That stays with you.

## Kept with your tabs

Whether a tab is a thread, its **Ask before sending to sub-threads** choice and which thread a tab belongs to are saved with the tabs, so they survive a restart. Work under way when Claudette closes isn't picked up again: a restored thread gets no report for it.

Closing a thread leaves its sub-threads open as ordinary tabs, and so does **Stop being a thread**. Neither sends a report for work under way. **Duplicate tab**, **Branch from here** and opening a copy from History start ordinary tabs.

## See also

- [Making a thread](making-a-thread.md)
- [Split panes](../tabs/split-panes.md): a thread beside its sub-threads.
- [The agent map](../plans/the-agent-map.md): for subagents.
