---
title: Folder trust
parent: Permissions and prompts
nav_order: 6
---

# Folder trust

A folder can carry its own Claude Code configuration: hooks, commands, environment variables, MCP servers. In a terminal, Claude Code asks before it works in a folder for the first time. When another app starts Claude Code, as Claudette does, it doesn't ask ([its permissions docs](https://code.claude.com/docs/en/permissions#what-runs-before-you-trust-a-folder) explain), and a cloned repository could run anything as its tab starts. So Claudette asks first, before Claude Code starts in a folder you haven't trusted.

## The question

The tab shows a card above the composer, *Trust this folder?*, listing what the folder's configuration would run, each item with the file that says so:

| Row | What it is |
|---|---|
| **Hook** | A hook in the folder's `.claude/settings.json` or `.claude/settings.local.json`, with its event and the command (or the address it sends to). |
| **Command** | A helper command, such as an API key helper or a cloud sign-in refresh. |
| **Environment variables** | The names an `env` block sets, not their values, which can be secrets. |
| **MCP server** | A server in a `.mcp.json` in the folder or a folder above it, with the command it starts or its address. |
| **Plugin** | A plugin the settings turn on. |
| **Skill with hooks** | A project skill with hooks in its frontmatter. |
| **Allowed without asking** | Allow rules in the folder's `.claude/settings.local.json`. |

The tab's row says it needs input while the card is up, and a message you send meanwhile goes once you've answered. A folder with nothing to list starts without asking. A folder that gains something later, say from a pull, is asked about at its next start.

## Answering

- **Trust this folder** starts Claude Code as usual and remembers the folder. Folders inside it are trusted too.
- **Start without it** starts Claude Code without the folder's own settings, its `.mcp.json` servers and its `CLAUDE.md`. A note in the conversation says so, and Claudette remembers the choice for the tab.
- **Close tab** closes it. For a pinned tab the button says **Unpin and close**.

To use the folder's configuration after **Start without it**, open the tab's menu and choose **Trust this folder**. Claude Code starts again with it.

## Which folders are trusted

Trust is kept on this computer and doesn't sync. The first time Claudette runs with this feature, it trusts the folders you already use: your open tabs', and your recent and favorite folders.

Claude Code's own trust, the one it records when you accept its terminal dialog, isn't read or changed. Trusting a folder in Claudette doesn't change how Claude Code treats the folder's shared allow rules, which it still holds back until the folder is trusted in Claude Code itself.

## Settings

Settings → Claude Code → **Ask before using a new folder's own configuration** (on by default). Turn it off and Claude Code starts in every folder without asking.

## See also

- [Your first tab](../getting-started/your-first-tab.md): meeting the question for the first time.
- [How Claudette uses Claude Code](../getting-started/how-claudette-uses-claude-code.md): what a tab loads from a folder once it's trusted.
