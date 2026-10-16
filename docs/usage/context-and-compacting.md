---
title: Context and compacting
parent: Usage and limits
nav_order: 5
---

# Context and compacting

Separate from your plan's limits, each tab has a context window: how much of the conversation Claude can hold at once. Claudette shows how full each tab's is, warns you when it's nearly full, and can compact it for you.

## The context indicator

The composer bar shows *Context 42%* for the selected tab. Hover over it for the numbers: *124,000 of 200,000 tokens · auto-compacts at 167,000*. It turns amber when the tab is close to the point where Claude Code compacts the conversation by itself (within 10% of it), or past 80% when auto-compact is off.

## The ring on a tab's row

Each tab's row in the sidebar has a small ring at its end that fills with the same percentage, so a tab nearing its limit stands out without being selected. It's muted, amber when the indicator warns, and red from 95%. It's hidden until the tab has context data, and the narrow rail doesn't show it. Click it to open the breakdown below without selecting the tab.

Settings → Appearance → **Show context on tab rows** (on by default) turns the rings off.

## What fills the window

Click the context indicator or the ring to see what's in the window, as Claude Code's `/context` shows it:

- The percentage and the numbers, then a bar of the whole window with each part as wide as its share, then the free space and the auto-compact buffer (hatched). Hover over a part for its name, tokens and share.
- A row for each part: **Memory files**, **Skills**, **System prompt**, **Custom agents**, **System tools**, **MCP tools** and **Messages**, each with its tokens and share. Tools Claude Code holds out of the window until they're needed are listed last, without a color.
- Under the rows, sections you can open: the memory files, the MCP servers (and how many of their tools are loaded), the custom agents, the skills, and the messages, split into *Tool results*, *Tool calls*, *Attachments*, *Your messages* and *Claude's replies*, then by tool. A section you leave open stays open when the breakdown refreshes after a turn.

The breakdown refreshes when the session starts and after each turn.

If this version of Claude Code can't say what's in the window, Claudette shows an estimate from the last call instead: how much is in the context and how much is free, with a note that it's estimated.

## Compacting

When the window is nearly full, the breakdown says *Nearly full. Claude Code will compact the conversation soon on its own.*

**Compact now** summarizes the conversation so far to free up context, like `/compact`. It's at the foot of the breakdown. The conversation gets a note when it's done: *Conversation compacted.* When Claude Code compacts it by itself, the note says so: *Claude Code compacted the conversation to free up context.*

What `/compact` and auto-compact do is Claude Code's own, and its [docs](https://code.claude.com/docs) describe them.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Show context on tab rows** | On | Settings → Appearance |

## See also

- [A tab's tokens](a-tabs-tokens.md) for what the tab has used in total.
- [The sidebar](../tabs/the-sidebar.md) for the other badges on a row.
