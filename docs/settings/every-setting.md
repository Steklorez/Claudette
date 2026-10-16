---
title: Every setting
parent: Settings
nav_order: 2
summary: "A table for each category in the Settings window, in the order its sidebar lists them."
---

# Every setting

A table for each category in [the Settings window](the-settings-window.md), in the order its sidebar lists them. Find a setting, then follow its link to the page that explains what it does.

The columns:

- **Setting** is its label, as the app shows it.
- **Default** is what a new install uses, and what the category's **Reset to defaults** puts back.
- **Syncs** says whether **Sync Claudette's settings through the library** carries it to your other machines ([Settings sync](../history/settings-sync.md)). **No** means it stays on this machine. The categories that sync are Appearance (all but Zoom and Detailed usage header), New tabs, Usage, Quick suffixes, Check-ins, Processes, Notifications and Keyboard.
- **Per tab** says whether [Tab settings…](../tabs/tab-settings.md) can override it for one tab. **Yes** means it can. **Own switch** means each tab has a switch of its own, which this setting sets for tabs you open from now on. **No** means every tab uses it.

## General

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Start Claudette when I log in** | Off | No (your OS keeps it) | No | [Starting at login](../away/starting-at-login.md) |
| **Confirm before closing a tab where Claude is working** | On | No | No | [Closing and restoring](../tabs/closing-and-restoring.md) |
| **Name each tab's session in Claude Code after the tab** | On | No | No | [Names, marks and pins](../tabs/names-marks-and-pins.md) |
| **Show Claude's service status** | On | No | No | [Claude is having problems](../troubleshooting/claude-is-having-problems.md) |
| **Messages sent while Claude works** | Send them into the turn (the other choice queues them) | No | No | [Sending and stopping](../composer/sending-and-stopping.md) |
| **Keep this computer awake** | Off | No | No | [Keeping the computer awake](../away/keeping-the-computer-awake.md) |
| **Only while Claude has work to finish** or **Whenever Claudette is running** | Only while Claude has work to finish | No | No | [Keeping the computer awake](../away/keeping-the-computer-awake.md) |
| **Remove worktrees once everything in them is merged** | Off | No | No | [Worktree tabs](../tabs/worktree-tabs.md) |
| **Remove worktrees no tab has used for** | Never (or 7, 14, 30 or 90 days), with **Clean up now** | No | No | [Worktree tabs](../tabs/worktree-tabs.md) |
| **Check for Claudette updates automatically** | On | No | No | [Claudette's updates](../updates/claudettes-updates.md) |
| **Include pre-releases** | Off | No | No | [Claudette's updates](../updates/claudettes-updates.md) |

The page also shows Claudette's version and update status, with **Check now**, **Download** and **Release notes**. Those are on [Claudette's updates](../updates/claudettes-updates.md).

## Sessions

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Also restore unpinned tabs when Claudette starts** | On | No | No | [Closing and restoring](../tabs/closing-and-restoring.md) |
| **Name for this machine** | The computer's name | No | No | [The session library](../history/the-session-library.md) |
| **Keep library sessions for** | Forever (or 1 month or 1 year) | No | No | [The session library](../history/the-session-library.md) |
| **Session library folder**, with **Browse…**, **Move library…** and **Use the default folder** | A folder in Claudette's data folder | No | No | [The session library](../history/the-session-library.md) |
| **Sync new tabs to the session library** | Off | No | Own switch (**Sync to other machines**) | [Syncing a tab](../history/syncing-a-tab.md) |
| **Sync Claudette's settings through the library** | Off | No | No | [Settings sync](../history/settings-sync.md) |

{: .note }
**Also restore unpinned tabs when Claudette starts** is on for new installs from the next release. Settings saved by 0.2.0 or earlier keep it off until you turn it on, or use this category's **Reset to defaults**.

## Processes

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Show the process monitor** | Off | Yes | Yes (**Process monitor**) | [Processes](../tabs/processes.md) |
| **Refresh the panel every (seconds)** | 2 (from 1 to 60) | Yes | No | [Processes](../tabs/processes.md) |
| **Show command lines** | On | Yes | No | [Processes](../tabs/processes.md) |

## Claude Code

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Check for Claude Code updates automatically** | On | No | No | [Claude Code's updates](../updates/claude-codes-updates.md) |
| **Path to claude** | Found automatically | No | No | [The first launch](../getting-started/the-first-launch.md) |
| **Fallback model** | None | No | No | [Model, effort and output style](../composer/model-effort-and-output-style.md) |
| **Keep copies of files Claude changes, so prompts can be rewound** | On | No | No | [Rewind and branch](../conversation/rewind-and-branch.md) |
| **Give Claude a task list** | On | No | No | [The Tasks page](../plans/the-tasks-page.md) |
| **Show every hook run in the conversation** | Off | No | No | [Thinking, notes and hooks](../conversation/thinking-notes-and-hooks.md) |
| **Summarize subagents' progress** | Off | No | No | [The agent map](../plans/the-agent-map.md) |
| **Suggest my next prompt** | Off | No | No | [Prompt suggestions](../composer/prompt-suggestions.md) |
| **Ask before using a new folder's own configuration** | On | No | No | [Folder trust](../permissions/folder-trust.md) |
| **Use my login shell's environment** (macOS and Linux only) | On | No | No | [Claude can't find a program](../troubleshooting/claude-cant-find-a-program.md) |
| **Connect new tabs to the Claude app** | Off | No | Own switch (**Connect to the Claude app**) | [The Claude app](../away/the-claude-app.md) |
| **Keep this computer awake while tabs are connected** | On | No | No | [Keeping the computer awake](../away/keeping-the-computer-awake.md) |

The page also shows the installed Claude Code and how it was installed, with **Check now**, **Update now**, **Update on next launch** and **What's new** ([Claude Code's updates](../updates/claude-codes-updates.md)), and the signed-in account with **Sign in** and **Sign out…** ([Your Claude sign-in](../accounts/your-claude-sign-in.md)).

## Accounts

{% include unreleased.html %}

This category is this machine's own. None of it syncs, and a tab can't override it. Accounts and their keys are on the [Accounts](../accounts/index.md) pages.

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **New folders use** | Default | No | No | [A folder's account](../accounts/a-folders-account.md) |
| **Add an API key**: a **Name** and the key | No accounts besides Default | No | No | [API-key accounts](../accounts/api-key-accounts.md) |
| An account's name and tag color | Chosen when you add it | No | No | [API-key accounts](../accounts/api-key-accounts.md) |
| **Warn when today's estimated spend passes ($)** and **Warn when this month's estimated spend passes ($)** | No warning | No | No | [Spend and its warnings](../accounts/spend-and-its-warnings.md) |
| **Change key…** and **Remove…** | | No | No | [API-key accounts](../accounts/api-key-accounts.md) |

## New tabs

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Default model** | Claude Code's default | Yes | Yes (**Model**) | [Model, effort and output style](../composer/model-effort-and-output-style.md) |
| **Default effort** | The model's default | Yes | Yes (**Effort**) | [Model, effort and output style](../composer/model-effort-and-output-style.md) |
| **Default permission mode** | Claude Code's default, named with the mode it gives | Yes | Yes (**Permission mode**) | [Permission modes](../permissions/permission-modes.md) |
| **Recent folders to keep** | 20 (from 1 to 100) | Yes | No | [Opening tabs](../tabs/opening-tabs.md) |
| **Clear recent folders** | | No | No | [Opening tabs](../tabs/opening-tabs.md) |
| **Favorite folders**, with **Add folder…** | None | No | No | [Opening tabs](../tabs/opening-tabs.md) |

Your favorite and recent folders belong to this machine. They stay put when settings sync is on, and **Reset to defaults** leaves them.

## Appearance

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Theme** | System | Yes | No | [Styles and themes](styles-and-themes.md#theme) |
| **Style** | Standard | Yes | No | [Styles and themes](styles-and-themes.md#style) |
| **Density** | Comfortable | Yes | No | [Styles and themes](styles-and-themes.md#density) |
| **Conversation font** | Claudette's default | Yes | No | [Styles and themes](styles-and-themes.md#fonts) |
| **Conversation font size** | 14 (from 9 to 28) | Yes | No | [Styles and themes](styles-and-themes.md#fonts) |
| **Code font** | Cascadia Mono, Consolas or Menlo | Yes | No | [Styles and themes](styles-and-themes.md#fonts) |
| **Code font size** | 13 (from 8 to 28) | Yes | No | [Styles and themes](styles-and-themes.md#fonts) |
| **Zoom** | 100% | No | No | [Accessibility](accessibility.md#zoom) |
| **Motion** | Follow the system | Yes | No | [Accessibility](accessibility.md#motion) |
| **Full-width conversation** | Off | Yes | No | [Reading the conversation](../conversation/reading-the-conversation.md) |
| **Quieter in-between steps** | On | Yes | No | [Reading the conversation](../conversation/reading-the-conversation.md) |
| **Show thinking expanded** | Off | Yes | No | [Thinking, notes and hooks](../conversation/thinking-notes-and-hooks.md) |
| **Show fun words while Claude works** | On | Yes | No | [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md) |
| **Show what Claude is doing while it works** | On | Yes | No | [The working line and running tasks](../conversation/the-working-line-and-running-tasks.md) |
| **Show when the prompt cache expires** | On | Yes | No | [The prompt cache](../usage/the-prompt-cache.md) |
| **Detailed usage header** | Off | No | No | [The detailed header](../usage/the-detailed-header.md) |
| **Show context on tab rows** | On | Yes | No | [Styles and themes](styles-and-themes.md#what-the-tab-rows-show) |
| **Show git branch on tab rows** | On | Yes | No | [Styles and themes](styles-and-themes.md#what-the-tab-rows-show) |
| **Show task progress on tab rows** | On | Yes | No | [Styles and themes](styles-and-themes.md#what-the-tab-rows-show) |
| **Show Claudette on the composer** | On | Yes | No | [Claudette on the composer](../claudette/claudette-on-the-composer.md) |
| **Size** (Claudette's) | Medium (or Small or Large) | Yes | No | [Claudette on the composer](../claudette/claudette-on-the-composer.md) |
| **How often** (Claudette does something) | Lively: every 10 to 30 seconds (or Calm: every 30 to 90) | Yes | No | [Claudette on the composer](../claudette/claudette-on-the-composer.md) |
| **Claudette shares tips** | On | Yes | No | [Claudette on the composer](../claudette/claudette-on-the-composer.md) |
| **Games with Claudette** | On | Yes | No | [Games with Claudette](../claudette/games-with-claudette.md) |

{: .note }
**Quieter in-between steps**, **Show when the prompt cache expires** and **Games with Claudette** are new in the next release.

## Usage

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Warn at (% of session used)** | 75 | Yes | No | [The usage header](../usage/the-usage-header.md) |
| **Alert at (% of session used)** | 90 | Yes | No | [The usage header](../usage/the-usage-header.md) |
| **Burn rate window (minutes)** | 30 (from 5 to 300) | Yes | No | [The usage header](../usage/the-usage-header.md) |
| **Show model-specific weekly limits** | On | Yes | No | [The usage header](../usage/the-usage-header.md) |
| **If Claude Code stops reporting them, read model limits from /usage** | Off | Yes | No | [The usage header](../usage/the-usage-header.md) |
| **Continue tasks when a usage limit resets** | On | Yes | Yes (**Auto-continue**) | [When a limit stops a task](../usage/when-a-limit-stops-a-task.md) |
| **Use usage credits once a limit is used up** | On | Yes | Yes (**Usage credits**, and the tab's menu) | [Tabs that don't use usage credits](../usage/tabs-that-dont-use-usage-credits.md) |
| **Keep usage history**, with **Clear usage history…** | 1 month (or 1 day, 1 week, 1 year or Forever) | Yes | No | [Usage history](../usage/usage-history.md) |
| **Share usage with my other machines** | Off | Yes | No | [Sharing usage across machines](../usage/sharing-usage-across-machines.md) |
| **Clear usage history on every machine…** (while sharing is on) | | No | No | [Sharing usage across machines](../usage/sharing-usage-across-machines.md) |

{: .note }
**Use usage credits once a limit is used up** and **Clear usage history on every machine…** are new in the next release.

## Quick suffixes

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| The list of suffixes, each with a label, its text and an optional shortcut, and **Add suffix** | Five: Clarify first, Plan only, Keep it small, Test it and Explain | Yes (as one list) | No | [Quick suffixes](../composer/quick-suffixes.md) |

## Check-ins

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Check in on long turns** | On | Yes | Yes (**On**) | [Check-ins on long turns](../composer/check-ins-on-long-turns.md) |
| **After the turn has run (minutes)** | 15 (0 turns it off) | Yes | Yes (**Run time**) | [Check-ins on long turns](../composer/check-ins-on-long-turns.md) |
| **After no output for (minutes)** | 5 (0 turns it off) | Yes | Yes (**Quiet time**) | [Check-ins on long turns](../composer/check-ins-on-long-turns.md) |
| **Message** | "Everything OK? Give me a one or two sentence status update: what you're doing, and whether you're stuck or waiting on something." | Yes | Yes | [Check-ins on long turns](../composer/check-ins-on-long-turns.md) |
| **Notify me when a check-in is sent** | Off | Yes | Yes | [Check-ins on long turns](../composer/check-ins-on-long-turns.md) |

## Diff tool

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Diff tool**: the built-in view, a tool installed here, or a custom command | Built-in diff view | No | No | [External diff tools](../changes/external-diff-tools.md) |
| **Command** (for a custom command), with **Test** | Empty | No | No | [External diff tools](../changes/external-diff-tools.md) |

## Editor

{% include unreleased.html %}

This category's page is headed Project Files.

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **The explorer is on the** | Left | No | No | [The explorer](../project-files/the-explorer.md) |
| **Wrap long lines** | Off | No | No | [Editing](../project-files/editing.md) |
| **Indent new lines with** | 4 spaces (or 2 spaces, 8 spaces or Tabs) | No | No | [Editing](../project-files/editing.md) |
| **Open files in** | Claudette's editor, at the line named (or the app the OS uses) | No | No | [Opening files from the conversation](../project-files/opening-files-from-the-conversation.md) |
| **Show a file clicked in a conversation** | Beside the conversation (or In its place) | No | No | [Opening files from the conversation](../project-files/opening-files-from-the-conversation.md) |
| **Open in the diff window** | Opens the file, as Open files in says (or in the app the OS uses, or the changes in the diff tool) | No | No | [The diff view](../changes/the-diff-view.md#opening-the-file) |

## Project tools

These stay on this machine, since program paths and the editors you have installed differ from one machine to the next.

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Default editor configuration** (Unreal Engine) | Development (or DebugGame) | No | No | [Unreal Engine](../project-tools/unreal-engine.md) |
| **Project files for** (Unreal Engine) | The OS's own: Visual Studio on Windows, Xcode on macOS, VS Code on Linux | No | No | [Unreal Engine](../project-tools/unreal-engine.md) |
| **Tell Claude about Unreal projects** | On | No | No | [Unreal Engine](../project-tools/unreal-engine.md) |
| **Default code optimization** (Unity) | Release (or Debug) | No | No | [Unity](../project-tools/unity.md) |
| **Tell Claude about Unity projects** | On | No | No | [Unity](../project-tools/unity.md) |
| **Godot executable**, with **Browse…** and **Detect** | Found automatically | No | No | [Godot](../project-tools/godot.md) |
| **Tell Claude about Godot projects** | On | No | No | [Godot](../project-tools/godot.md) |
| **Open solutions with** | The OS's default app (or Rider, Visual Studio, VS Code or another program) | No | No | [Unreal Engine](../project-tools/unreal-engine.md) |

## Notifications

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **A tab finishes its turn** | On | Yes | No | [Notifications](../away/notifications.md) |
| **A tab needs permission or an answer** | On | Yes | No | [Notifications](../away/notifications.md) |
| **A tab's Claude Code stops with an error** | On | Yes | No | [Notifications](../away/notifications.md) |
| **Usage crosses a warning level, is on pace to run out, or resets** | On | Yes | No | [Notifications](../away/notifications.md) |
| **Claude Code needs me to sign in** | On | Yes | No | [Notifications](../away/notifications.md) |
| **A Claude Code update is ready** | On | Yes | No | [Notifications](../away/notifications.md) |
| **A project action finishes** | On | Yes | No | [Notifications](../away/notifications.md) |
| **Show the number of tabs needing input on the Dock or taskbar icon** | On | Yes | No | [The Dock and taskbar icon](../away/the-dock-and-taskbar-icon.md) |
| **Animate the Dock or taskbar icon while tabs are working or waiting** | On | Yes | No | [The Dock and taskbar icon](../away/the-dock-and-taskbar-icon.md) |

The notification for a check-in is **Notify me when a check-in is sent**, under Check-ins.

## Keyboard

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Send with** | Enter, with Shift+Enter for a new line | Yes | No | [Keyboard](keyboard.md#send-with) |
| Each command's shortcut | The defaults in [Keyboard shortcuts](../reference/keyboard-shortcuts.md) | Yes | No | [Keyboard](keyboard.md#changing-a-shortcut) |

## Perforce

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Keep Perforce logins fresh** | Off | No | No | [Perforce](../project-tools/perforce.md) |
| **Password source** | Stored by Claudette (recommended) | No | No | [Perforce](../project-tools/perforce.md) |
| **Renew the ticket when less than this is left (minutes)** | 30 | No | No | [Perforce](../project-tools/perforce.md) |
| **Request tickets valid on all hosts (p4 login -a)** | Off | No | No | [Perforce](../project-tools/perforce.md) |
| **Show changelist on tabs** | Off | No | No | [Perforce](../project-tools/perforce.md) |
| **Stored password**, with **Save password** and **Forget password** | None | No | No | [Perforce](../project-tools/perforce.md) |
| **Per-folder server and user**, with **Add folder…** | None | No | No | [Perforce](../project-tools/perforce.md) |

## Advanced

| Setting | Default | Syncs | Per tab | Explained in |
|---|---|---|---|---|
| **Extra arguments for every claude process** | Empty | No | No | [Advanced](advanced.md#extra-arguments-for-claude) |
| **Log protocol traffic** | Off | No | No | [Advanced](advanced.md#log-protocol-traffic) |
| **Open log folder** and **Open data folder** | | No | No | [Advanced](advanced.md#the-log-and-data-folders) |
| **Diagnostics** | | No | No | [Advanced](advanced.md#diagnostics) |

## The project's pages

The **Links**, **Actions** and **Tools** pages below the categories aren't in this list. They belong to a project rather than to Claudette: [the Settings window](the-settings-window.md#the-projects-pages) says how they work, and [Actions and links](../project-tools/actions-and-links.md) what they hold.
