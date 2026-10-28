---
title: MCP servers
parent: Tabs and sessions
nav_order: 13
summary: "MCP servers give Claude extra tools."
---

# MCP servers

MCP servers give Claude extra tools. Claude Code runs them, from your own and the project's configuration: see [Claude Code's MCP documentation](https://code.claude.com/docs/en/mcp). Claudette shows each tab's servers in the **MCP** tool window, and lets you reconnect, turn off and sign in to them without leaving the window.

## The MCP page

The page shows for a tab whose session has MCP servers. Choose the **MCP** [tool window](tool-windows.md), or use **Show MCP servers** in the [command palette](the-command-palette.md).

Each server shows its name, how it's connected, where it's configured and its version when Claude Code says, and the error for one that failed. The state is one of:

- *Connected · 12 tools*
- *Connecting…*
- *Needs signing in*
- *Failed*
- *Off*

The server's name and its error are the server's own text, shown as text. A row lights up under the pointer, so it's plain which server its buttons are for.

The page reads the list from Claude Code when it opens and after each change. **Refresh** reads it again. When a server has failed or needs signing in, the page's tab shows a warning dot, with *1 needs attention* as its tip.

## Reconnecting a server

**Reconnect** shows on a server that failed, needs signing in or is still connecting. Choose it to ask Claude Code to connect the server again, for example after you start the program it talks to, or after you sign in.

## Turning a server off and on

**Turn off** disconnects a server and removes its tools from the session. **Turn on** puts it back. This is for this session only: your own and the project's MCP settings don't change.

## Signing in to a server

1. On a server that says *Needs signing in*, choose **Sign in**. Claudette opens the server's sign-in page in your browser. Only web addresses are opened.
2. Finish in the browser. Claude Code waits on this computer for the browser to come back, then connects the server by itself. **Refresh** shows it.
3. If the page the browser ends on doesn't load, paste its address into the box that appears under the row (*If the page after signing in didn't load, paste its address here*) and choose **Done**. **Open the sign-in page again** is there too.

**Sign out** shows on a connected remote server (one reached over HTTP or SSE). It makes Claude Code forget the sign-in, so connecting the server again asks you to sign in again.

These requests aren't part of Claude Code's documented interface, so one Claude Code refuses says so, with *You can sign in with /mcp in Claude Code in a terminal.*

## When a server asks you something

A server can ask for input in the middle of a turn, with a form in the conversation. That's covered in [MCP servers asking for input](../permissions/mcp-servers-asking-for-input.md).

## See also

- [Tool windows](tool-windows.md), for the other windows.
- [Processes](processes.md), which lists a server's process while the process monitor is on.
