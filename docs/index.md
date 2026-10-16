---
title: Home
nav_order: 1
description: Claudette puts Claude Code in a native desktop window, with a tab for each session and your plan's usage always on screen.
permalink: /
---

# Claudette

Claudette puts [Claude Code](https://code.claude.com/docs) in a real desktop window: every session in its own tab, every change ready to review, and your plan's usage on screen the whole time. It isn't a reimplementation. Each tab runs the real `claude`, with your settings, `CLAUDE.md`, MCP servers, hooks and permissions, and Claudette draws it as native UI instead of a terminal. It runs on Windows and macOS, and from source on Linux.

{% include shot.html name="standard" alt="Claudette's window: seven tabs over three game projects in the sidebar, a conversation with tool calls and a code block, the Changed files page in the side panel, the plan's usage in the header, and Claudette herself standing on the message box" %}

[Download Claudette](downloads.md){: .btn .btn-primary .mr-2 } [Get started](getting-started/){: .btn }

{% assign latest = site.github.latest_release %}
{% if latest and latest.tag_name %}
The latest version is **{{ latest.tag_name | remove_first: "v" }}**, from {{ latest.published_at | date: "%-d %B %Y" }}: [what's new in it]({{ latest.html_url }}).
{% endif %}

## Where to start

- New to Claudette? [Getting started](getting-started/) takes you from installing it to your first tab, with [a tour of the window](getting-started/a-tour-of-the-window.md).
- Running a lot of sessions? [Tabs and sessions](tabs/) and [Threads](threads/).
- Want to know what Claude did? [The conversation](conversation/) and [Reviewing changes](changes/).
- Watching your plan? [Usage and limits](usage/).
- Looking for a key or a setting? [Reference](reference/) and [Every setting](settings/every-setting.md).

## What's in this guide

| Chapter | What it covers |
|---|---|
| [Getting started](getting-started/) | What you need, installing, signing in, your first tab, and a tour of the window. |
| [Tabs and sessions](tabs/) | Opening, grouping, marking and closing tabs, the side panel, split panes, worktree tabs and the process monitor. |
| [The conversation](conversation/) | Reading the conversation, tool calls, subagents, finding your way, copying, rewinding, images and links. |
| [The composer](composer/) | Sending and stopping, slash commands and mentions, attachments, drafts, quick suffixes, the model and effort. |
| [Permissions and prompts](permissions/) | Permission cards and modes, questions, plans to approve, MCP servers' forms and folder trust. |
| [Plans, tasks and agents](plans/) | The Tasks page, Claude Docs and the agent map. |
| [Threads](threads/) | Spreading one job over several tabs, and sessions that message each other. |
| [Reviewing changes](changes/) | Changed files, the diff view, reviewed files, reverting, and your own diff tool. |
| [Project files](project-files/) | A project's explorer and editor, and Claude's changes in it. |
| [Usage and limits](usage/) | The usage header, the Usage window, context, the prompt cache, alerts, and what happens at a limit. |
| [Accounts](accounts/) | Your Claude sign-in, API-key accounts and their spend. |
| [History and syncing](history/) | History, the session library, and picking up on another machine. |
| [Project tools](project-tools/) | Unreal Engine, Unity, Godot, your own actions and links, and Perforce. |
| [Away from your desk](away/) | Notifications, the Claude app, keeping the computer awake and starting at login. |
| [Claudette](claudette/) | Claudette on the composer, and her games. |
| [Settings](settings/) | The Settings window, every setting, styles, accessibility and the keyboard. |
| [Updates](updates/) | Claude Code's updates and Claudette's. |
| [Troubleshooting](troubleshooting/) | What to do when something goes wrong. |
| [Privacy and your data](privacy/) | What Claudette keeps, where, and what leaves your computer. |
| [Reference](reference/) | Shortcuts, icons, notifications, the command line and `claudette.json`. |

Claudette is made by [dotBunny](https://dotbunny.com) for its own use, and shared as it is: it's opinionated, and keeps Claude's token usage down by default. It's open source, on [GitHub](https://github.com/dotBunny/Claudette).
