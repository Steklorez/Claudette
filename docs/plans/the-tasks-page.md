---
title: The Tasks page
parent: Plans, tasks and agents
nav_order: 1
summary: "When Claude keeps a task list, writes a plan, or writes a doc on claude.ai, a Tasks tool window comes, and the tab shows how far the work has got, such as 2 of 5."
---

# The Tasks page

When Claude keeps a task list, writes a plan, or writes a doc on claude.ai, a **Tasks** tool window comes, and the tab shows how far the work has got, such as *2 of 5*. The page shows the plan, each task with who has it and what it did, and the docs. Claude's own task list and Plan mode are Claude Code's; this is what Claudette shows of them.

{% include shot.html name="tasks-standard" alt="The Tasks page of a thread: the plan in its second version with its changes from the first, seven tasks with two done and who has each, the thread's sub-threads under it in the sidebar, and SendMessage calls in the conversation." %}

## Opening the page

- Click the **Tasks** tool window's tab. It shows only while the tab has tasks, a plan or a doc.
- Click the progress text at the right of the status line, or the *3/7* badge on the tab's row.
- Choose **Show tasks** in the [command palette](../tabs/the-command-palette.md).

## The tasks

Each task shows its state (to do, in progress, done), its number as Claude refers to it (*#3*), its subject and its description. While a task depends on others that aren't done, it says *Waiting on #1, #2*. The task in progress shows what Claude is doing, such as *Running the tests*, in bold.

Under a task, one line says who is working on it when Claude says (a subagent), how long it took and what it used: *Explore · Took 4m · 3 files · 120k tokens*, or *Started 14:05* while it's under way. **3 files** lists the files Claude changed while the task was in progress, and each opens in the diff view as [Changed files](../changes/changed-files.md) shows it. Work a subagent does counts toward its parent's task. Clicking a task that has started goes to where it started in the conversation.

Claude's task list also shows pinned at the top of the conversation, under the line *To-do · 2 of 5 done*. A long list scrolls once it takes two fifths of the conversation's height, so the conversation keeps room under it. Click that line to close the list when it's long: closed, it's the one line, naming the task in progress, such as *To-do · 2 of 5 done · Running the tests*. It stays closed as Claude works through the tasks, and when Claudette restarts, until you open it again. It opens by itself when Claude starts a new list, and closes once every task is done.

## Tasks in the conversation

When a task goes in progress, a slim row marks the spot in the conversation: *◐ #3 Run the migration*, with *Started 14:05* at its right, then ☑ and *Took 4m* once it's done. A subagent's task has its row in the subagent's group. Click a row to open this page.

When a turn that changed the task list ends, a **Tasks** card says where the list stands: *All 7 tasks done · 23m*, or *4 of 7 tasks left · Next: #4 Run the migration*. While tasks are left it has **Continue**, which sends *Carry on with the tasks that are left.* as your message. **Continue** is on the latest such card only, until the next turn starts. The [notification](../away/notifications.md) for a finished turn starts with the same words. More about the end of a turn is in [The end of a turn](../conversation/the-end-of-a-turn.md).

## The plan

The plan is above the tasks, as formatted text. It's the newest of:

- The plan Claude is writing in Plan mode, as it writes it, headed *Drafting since 14:02*.
- A plan Claude put up for review, with what became of it: *Waiting for review*, *Approved 14:05*, *Sent back 14:03* with your feedback under it, or *Not answered* if the turn ended first. [Approving a plan](../permissions/approving-a-plan.md) covers the card.
- A reply you chose as the plan (below), with when Claude wrote it: *From Claude's reply at 14:03*.

Every plan is kept as a version, numbered in the session. With more than one, the header says which you're looking at (*v2 of 3*), **‹** and **›** step through them, and **Changes from v1** shows the version as a line diff against the one before. A new version shows as it comes, unless you've stepped back to an earlier one.

### Using a reply as the plan

Sometimes Claude writes its plan in a reply rather than in Plan mode, and you approve it by answering "go". To show it on this page, click the plan icon beside the reply's time and **Copy**, or right-click the reply and choose **Show as the plan**. It's saved with the tab.

When Claude starts a new task list and its latest reply looks like the plan, with a list of three or more items, the pinned list asks *Use Claude's reply from 14:03 as the plan?* with **Use as the plan** and **×** to say no.

## Progress elsewhere

- **The status line**, under the composer, says at its right *Drafting the plan…* while Claude writes one, *3 of 7 tasks · Running the tests* while tasks are left, *All 7 tasks done* when they are, and *Following the plan* once a plan is approved or a reply is shown as the plan. Clicking it opens this page.
- **The tab's row** has a *3/7* badge while tasks are left, with the task in progress in its tip, and its second line names the task Claude is working on in place of the model and effort. See [The sidebar](../tabs/the-sidebar.md).
- **The tab info card** has a *Tasks* row, *3 of 7 done · Now: Running the tests* ([The tab info card](../tabs/the-tab-info-card.md)).

`/clear` empties the page, versions and all. A tab that comes back after a restart rebuilds the tasks, with their dates, from its conversation; a plan that was sent back may return without your feedback.

## Settings

- Settings → Claude Code → **Give Claude a task list** (on by default). Claude Code gives its task-keeping tools only to some models; newer ones keep track of their work without a written list, so they'd make no tasks and only the plan would show. This gives every model the tools, at the cost of the room their definitions take in the context. Off, Claudette leaves it as your environment has it. It applies to tabs started after the change.
- Settings → Conversation → Tab rows → **Show task progress** (on by default) shows the *3/7* badge and the task on the row's second line.

## See also

- [Claude Docs](claude-docs.md): docs Claude writes also appear on this page.
- [The agent map](the-agent-map.md): what subagents are doing.
- [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md): tasks Claude Code keeps running in the background are a different thing.
