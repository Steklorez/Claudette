---
title: Claude Docs
parent: Plans, tasks and agents
nav_order: 2
---

# Claude Docs

Claude can write a doc on claude.ai with claude.ai's **Claude Docs** connector, which comes from your claude.ai account: a task list to work through, a report, notes to keep. The doc lives on claude.ai, where you can read, edit and comment on it. Claudette keeps track of the docs a tab's Claude makes and gives you a link to each.

You don't set anything up. If your account has the connector and Claude uses it, the links appear.

## Where the links are

- **On the call.** The tile of a call that made a doc, edited it or commented on it has a link under its header: *Created "Enemies: your task list"*, *Edited "…"*, *Commented on "…"*, or *Edited a Claude Doc* when the tab hasn't seen the doc's title. Click it to open the doc in your browser. [Tool calls](../conversation/tool-calls.md) explains the tiles.
- **At the end of the turn.** A **Claude Doc** row for each doc the turn made or changed, once per doc, as the most it did (made it, else edited it, else commented on it): *Created "Enemies: your task list"* and **Open**. It appears however the turn ended, so a stopped turn's doc is there to open too. See [The end of a turn](../conversation/the-end-of-a-turn.md).
- **On the Tasks page.** A **Docs** section above the plan lists each doc's title, which opens it, and *Created 14:02 · edited 14:30*. A doc alone is enough to bring the page, and the page's tip says *A Claude Doc* (or *2 Claude Docs*) when that's all it has. See [The Tasks page](the-tasks-page.md).

A doc Claude only edits isn't listed until a reply says where it is, as a comment's does.

Find in the conversation looks through the links and rows, and an [export](../conversation/exporting.md) keeps the link on the call's line. A tab that comes back after a restart lists its docs from its conversation, and `/clear` empties them with the rest of the Tasks page.

## See also

- [The Tasks page](the-tasks-page.md): where the docs are listed.
