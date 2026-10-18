---
title: Slash commands and mentions
parent: The composer
nav_order: 3
summary: "Type / or @ in the message box and a list opens above it: / for Claude Code's commands, @ for files, folders and other sessions."
---

# Slash commands and mentions

Type `/` or `@` in the message box and a list opens above it: `/` for Claude Code's commands, `@` for files, folders and other sessions. The text box keeps the focus, so you carry on typing to narrow the list.

## Slash commands

`/` at the very start of a message lists the commands the session accepts, under the heading **Commands**: Claude Code's built-in ones, your own, the project's, plugins' and MCP servers'. Only the start counts, because that's the only place Claude Code runs a command.

- Each entry shows the command's name, what it takes, such as an argument hint, and a description.
- Typing narrows the list, ignoring case. Names that start with what you typed come first, then aliases (`/reset` finds `/clear`), then names that contain it and, from three letters, descriptions.
- Picking one replaces the word with `/name ` and closes the list. Claude Code runs the command when you send the message, as it does in a terminal.
- Commands that only work in a terminal, such as `doctor`, aren't offered.
- The list updates when Claude Code finds new commands mid-session, such as skills in a subfolder. A new tab says *Claude Code hasn't listed its commands yet.* until Claude Code has. A word that matches nothing says *No matching commands*.

What the commands do is Claude Code's: see [Claude Code's commands](https://code.claude.com/docs/en/slash-commands) and [skills](https://code.claude.com/docs/en/skills). A message that starts with `/` and has [quick suffixes](quick-suffixes.md) on it handles them differently from a plain message, which that page explains.

## Files and folders

`@` at the start of a word lists the tab's working folder, under **Files and folders**.

- In a git repository the list follows `.gitignore`. Elsewhere Claudette walks the folder, skipping `.git`, `node_modules`, `bin`, `obj` and similar, and stops at 20,000 entries.
- Typing narrows it, ignoring case. The best matches are a name that starts with what you typed, then a path that does (`src/de` finds `src/deep/`), then a path segment, then a name or path that contains it, then its letters in order, preferring the starts of words (`tvm` finds `TabViewModel.cs`). With nothing typed, the top of the folder is listed, folders first.
- Picking a file inserts `@path/to/file `, relative to the working folder. Picking a folder inserts `@folder/` and lists what's inside. A path with spaces is quoted: `@"docs/my notes.md"`.
- A word that matches nothing says *No matching files*, and an empty folder *No files in this folder*.

When you send the message, Claude Code reads each mentioned file and gives Claude its content, with no permission prompt. A folder becomes a listing, and an image file becomes an image. Absolute paths outside the folder work too. A path that doesn't exist stays as text. See [Claude Code's common workflows](https://code.claude.com/docs/en/common-workflows).

## Typing a path

Type a folder after `@`, up to a slash, and the list shows what's in it: folders first, then files, narrowed to the names that start with what you type after the slash.

- **Anywhere on this computer.** A path that starts with a drive or a share on Windows (`@C:\Work\`, `@D:/`, `@\\server\share\`), with `/` on macOS and Linux, with `~/` for your home folder, or with `../` lists that folder and nothing else, under the folder's own path, such as *D:/Work/Game/*. Picking a folder carries on into it. Picking a file inserts its full path, with forward slashes: `@C:/Users/sam/notes.md`. A folder that isn't there says *No such folder*, and an empty one *Nothing in this folder*.
- **In the project.** A folder in the working folder (`@Source/`, or `@./` for the top) lists its own entries ahead of the usual matches, with what the list otherwise leaves out: the files git ignores (outside git, the folders it skips), dimmed, as the [Project Files explorer](../project-files/the-explorer.md) shows them.
- A folder is read once as you reach it, and kept for a few seconds, so typing in it is instant. A slow or network drive says *Listing D:/Work/…* while it's read, rather than holding up the message box.

## The tab's extra folders

The files in the tab's [extra folders](../tabs/tab-settings.md) are in the list too, matched with the working folder's, each with its folder's name before its path (*shared/lib/*). With nothing typed, each extra folder is listed after the top of the working folder, as *Extra folder*. Picking one of their files or folders inserts its full path, since it isn't in the working folder.

## Sessions, tabs and agents

The same `@` list starts with the sessions Claude can message, up to eight, and its heading reads **Sessions and files**. Each is marked by what it is:

- *Sub-thread*, one of this thread's sub-threads.
- *Tab*, another tab of Claudette's.
- *Session · C:\work\api*, another Claude Code session on this machine, such as a terminal's, with its folder.
- *Subagent · Explore*, one of this tab's subagents, with its type.

Picking one inserts `@name `. When you send, Claudette tells Claude how to reach each one, and the card shows *Told Claude how to reach Art page* under your message. The details are in [Reaching other sessions](../threads/reaching-other-sessions.md), and threads in [What threads are](../threads/what-threads-are.md).

## Keys

- `Up` and `Down` move through the list. `Enter` or `Tab` picks, and clicking an entry does too.
- `Esc` closes the list without stopping Claude. It stays closed while the caret is in that command or mention.
- `Enter` still sends when the list has nothing to pick, or when what you typed is already the highlighted entry, such as `/compact` in full.

## See also

- [Attachments and large pastes](attachments-and-large-pastes.md), which turn dropped files into `@` mentions.
- [Earlier prompts, drafts and the stash](earlier-prompts-drafts-and-the-stash.md): with the list open, `Up` and `Down` move in the list rather than through earlier prompts.
