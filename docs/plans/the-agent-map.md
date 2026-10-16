---
title: The agent map
parent: Plans, tasks and agents
nav_order: 3
summary: "When Claude fans work out to several subagents, possibly nested, the conversation shows each as a group (Subagents)."
---

# The agent map

When Claude fans work out to several subagents, possibly nested, the conversation shows each as a group ([Subagents](../conversation/subagents.md)). That's fine for one at a time and hard to follow when several run at once. The agent map is a live list of what a tab's subagents are doing. Subagents are Claude Code's ([its docs](https://code.claude.com/docs/en/sub-agents) describe them).

{% include shot.html name="agents-standard" alt="The Agents page with the agent map: three Explore subagents, the first selected, with the prompt it was given and its report below." %}

## Opening it

- Click **Agents** in the side panel's page tabs. It shows a busy dot while a subagent runs.
- Click the **Agents** button in the [composer bar](../composer/the-composer-bar.md), which appears once the tab has subagents. It says *2 agents running*, or *Agents (3)* once they've finished.
- Choose **Show the agent map** in the [command palette](../tabs/the-command-palette.md).

The page's header sums the tree up, for example *2 agents running (1 waiting on you); 2 done*. With none, it says *Claude hasn't started any subagents in this tab.* The tab's info card has an **Agents** row too ([The tab info card](../tabs/the-tab-info-card.md)).

To give a big fan-out more room, choose **Open the agent map in a window** at the top of the page: the tree and the details sit side by side. There's one window per tab, and it closes with the tab.

## The tree

The main agent is the root, and each subagent sits under the agent that started it, so nesting shows. Branches can be collapsed. An agent that has finished, with nothing under it still going, folds its branch away, so a long session's tree stays short. A prompt from inside it opens it again.

Each row has two lines:

- A status mark, the task description, and the running time.
- The agent's type, such as `Explore` or `general-purpose`, then what it's doing now: its latest tool call (*Grep auth in src/*) or the first line of its latest text. Once it's finished, the first line of its report, or how it ended. A subagent in the background says *Running in the background*.

The marks:

| Mark | Meaning |
|---|---|
| A pulsing dot | Running |
| **!** | Waiting on a permission prompt; the row is highlighted |
| A green dot | Done |
| **✕** | Failed |
| **■** | Stopped |

The main agent's row shows whether a turn is running and its latest tool call.

## Details

Click a row, or press `Enter` on it, to scroll the conversation to that subagent's group and open it, along with any groups it's nested in. Below the tree, the page shows:

- Its status, type, model, running time, tool calls and tokens. The tokens are the subagent's latest request, roughly its context, not a running total.
- **Prompt**: what it was given, with **Copy**.
- **Result**: what it returned to its parent, with **Copy**, once it has finished.
- **Show in conversation**, which becomes **Go to the prompt** while it waits on you.

The same actions, with **Copy prompt** and **Copy result**, are on a row's right-click menu.

When a subagent is waiting on a prompt, its row is highlighted and clicking it goes to the prompt. The prompt's card names the subagent asking ([Permission cards](../permissions/permission-cards.md)).

## Stopping a subagent

A subagent that can be stopped has a **■** at the right of its row, shown while the pointer is over the row or it's selected. The details have **Stop subagent…** too, and so does the right-click menu. Claudette asks first. Claude Code then stops that subagent and any it started, tells Claude it was stopped, and the rest of the turn carries on.

When Claude Code hasn't given a subagent an ID to stop it by, the button says **Stop turn…** instead and stops the whole turn, like **Stop** does ([Sending and stopping](../composer/sending-and-stopping.md)).

## Tabs that come back

After a restart, the map shows the finished tree, with no live status, clock or stop buttons. Subagents' own tool calls come back into their groups. One with no recorded result says *Didn't finish*.

## Settings

- Settings → Claude Code → **Summarize subagents' progress** (off by default). Claude Code writes a line about how far each subagent has got, shown in place of its latest tool call while it runs. Each summary is a small extra model call, which counts toward your usage. It applies to tabs started after the change.

## Notes

- Claude Code limits how deep subagents nest, three layers by default, so the tree may be shallower than you expect. That limit is Claude Code's own configuration.
- Agent teams, which Claude Code only runs in its interactive mode, aren't something a tab can have. A subagent Claude names runs as an ordinary one and shows here like any other.

## See also

- [Subagents](../conversation/subagents.md): the subagent's card in the conversation.
- [Threads](../threads/index.md): separate tabs that work on one task, which you can step into.
