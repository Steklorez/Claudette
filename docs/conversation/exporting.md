---
title: Exporting
parent: The conversation
nav_order: 9
---

# Exporting

**Export conversation…** saves a tab's conversation as a file you can read or share.

## Export a conversation

1. Open the tab's menu (right-click its row in the sidebar) and choose **Export conversation…**. The command palette has it too ([The command palette](../tabs/the-command-palette.md)).
2. In the save dialog, pick **Markdown** (`.md`, the default) or **Web page** (`.html`, one file with its style inside it). The name you give decides which.
3. Save. The tab says where the file went.

The file is written under a temporary name and then renamed, so a failed export never leaves half a file under the name you chose.

## What's in it

- Your prompts and Claude's replies in full, with their times. A reply's Markdown is kept as written. In the web page it is shown as text, so nothing in it runs.
- Each tool call as one line with its result's summary, and a Bash command with the start of its output (the first 4,000 characters).
- A call that made or changed a Claude Doc has the doc's link instead.
- A subagent as one line with its report.
- The outcomes of permission cards, a plan's text, where each task started (*Started task #3: Run the migration*), failed hooks and errors.

## What's left out

Thinking, and Claudette's other notes.

## See also

- [Copying and quoting](copying-and-quoting.md) to take just a part of a conversation.
- [The scratch pad](the-scratch-pad.md), which an export doesn't include.
- [History](../history/history.md) to open an old session again rather than save it.
