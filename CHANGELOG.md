# Changelog

What's changed in each version of Claudette, for the people using it.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, a new feature or a change you'd notice raises the second number (0.2.0 to 0.3.0), and fixes alone the third (0.3.0 to 0.3.1).

## [Unreleased]

### Added

- **Editor logs under the tab.** While the Unreal editor, a commandlet or a game run has the tab's project open, its log is a row under the tab, and a click shows it in the main area as a project run's log is, followed as it's written, with its errors and warnings colored and counted on the row. Unreal's lines show in columns: the time in your computer's clock, the log category, the verbosity and the message. A filter over the log hides levels with a click on their chips, each with its count, Unreal's categories from a menu, or every line without some text; the levels and categories you hide are remembered for the project. Unity's editor and batch runs, and Godot's editor and game, get theirs too. The row stays after the editor closes, says *Crashed* when its log ends in a crash, and comes back with the new log when the editor starts again. Claudette reads the file without keeping it open, so the editor can rename or delete it as it always does. **Show editor logs under the tab** in Settings → Project tools turns it off.
- **Collapse a tab in the sidebar.** A tab with rows under it, a thread's sub-threads, subagents or a project action's runs, has a chevron beside its name that folds them away, as a project group's does; **Collapse tab** in its menu or the command palette does the same. The collapsed row says how many it hides and shows the most urgent of them, such as a sub-thread waiting on you or a failed build. It opens again when you go to one of them, when a sub-thread joins, or when one starts waiting on you, and it stays collapsed when Claudette restarts.

### Changed

- **Stay current and Wrap lines over a project action's log.** **Stay current** shows whether the log is following its newest line: scroll up and it turns off, so the log holds still while you read; scroll back to the end, or click it, and it follows again. Turn **Wrap lines** off and long lines stay on one line, scrolling sideways. Each new log starts as you left the last one.

## [0.4.0] - 2026-10-18

### Added

- **`@` completes any path.** Type a folder after `@` and the list shows what's in it, from `C:\`, `/`, `~/` or `../` too, folder by folder, and picks a file outside the project in full. A folder in the project shows the files git ignores as well, dimmed, and the tab's extra folders' files are in the list with the project's.
- **Subagents under their tab.** Each subagent Claude starts has a row of its own under its tab, nested under the one that started it and dimmed once it's finished and from an earlier turn. Select one to see its work in the tab, with the prompt it was given and what it handed back; the conversation keeps a one-line card for it. You can't write to a subagent, so **Message it through Claude** names it in your next message instead, and a permission prompt it waits on is answered right there. The Agents page opens a subagent's work the same way. To open subagents' cards in the conversation as before, set Settings → Appearance → **Show subagents' work** to **In the conversation**.
- **Images open in Claudette.** Click an image file Claude names (a PNG, JPEG, GIF, WebP or BMP), its **Open** on a tool card or in Changed files, or a link to an image on the web, and it opens in Claudette's image window rather than another app. In Project Files, double-click an image, or use **Open in Claudette's image viewer** where a file says it isn't text. To open them in your system's app as before, set Settings → Editor → **Open images in** to **The app the OS uses for the file**.
- **Animated GIFs play.** An animated GIF or WebP plays in the image window, with **Pause** and **Play**. With motion reduced, it opens still until you press **Play**.
- **Discourage subagents, or turn them off.** Settings → Claude Code → **Subagents**, or one tab's **Tab settings…**: **Discourage them** asks Claude to do the work itself unless you ask for a subagent, and **Don't allow them** turns down any it starts, in tabs already open too. Claude hears about a change with your next message.

### Changed

- **A project action's log shows where the conversation is.** Clicking an action's entry under its tab shows its log in the main area, with room to read a build's errors, and the tab's own row takes you back to the conversation. **Open beside** in the entry's menu, or dragging it onto the conversation, puts the log beside it. Web addresses in the log open in your browser, and the files and folders it names open with a click, in Claudette's editor at the line an error points to. Select and copy any of it, and `Ctrl/Cmd+F` finds in it. **Show output…** in the project's menu and the *action finished* notification open it there too, and the side panel's **Project** page keeps the project's details.

### Fixed

- Just as Claude finished a turn, turning Remote Control on or off, moving the tab to another account, changing its extra folders, ticking **Sync to other machines**, or sending a message with *Queue them* chosen could wait for a turn that had already ended. A queued message, or a sub-thread's report, that went as the turn ended could leave the tab saying Claude was idle while it worked.
- When Claude Code started a turn by itself, such as for a message from the Claude app, a message sent with *Queue them* chosen, or **Auto-continue**'s message once a usage limit reset, could go in that moment as if Claude were idle.
- As the working line's glyph twinkled, the line could nudge up and down, taking the conversation above it along. On Windows, one of its stars also came out smaller and thinner than the rest.
- After Claudette restarted, such as for an update, a subagent Claude ran in the background could read *Didn't finish* when it had finished while Claude was still working.

## [0.3.0] - 2026-10-16

### Added

- **Project Files**: a tab for each project with an explorer and a code editor, opened with the file button beside a group's **+**, or **Go to file…** (`Ctrl/Cmd+P`). Find a file by name, or make a new file or folder from the explorer's menus, then edit it and save it; unsaved edits are kept when Claudette closes. Claude's changes are marked in the gutter, with a peek to see what was there and **Revert** it. A file named in the conversation opens in it at the line, beside the conversation, which gets its width back when you close the file. The diff window's **Open** brings it in front of the diff, which closes. Settings → Editor sets it up, or sends files to the OS's app instead, and **Open in the diff window** can open the file in the OS's app, or the changes in your diff tool.
- **API-key accounts.** Add a key in Settings → Accounts → **Add an API key**; it's kept only in the OS's credential store. A project group's **Account ▸** menu picks the account its new tabs use, and a tab's moves it to another, saying first what the move costs. While one of its tabs is selected, the header shows the account's estimated spend today, this week and this month, and Settings → Accounts can warn when a day or a month passes an amount.
- **Files named in Claude's replies open.** A Markdown link to a file, or a file's name in code (`Player.cpp:42`), opens the file with a click. Several files with that name are offered in a menu.
- **Tabs side by side.** **Open beside** in a tab's menu, or drag its row onto the conversation: up to three tabs at once, each with its own conversation and composer. Drag the line between two to share the width differently. A thread's **Thread ▸** → **Show with its sub-threads** shows it beside its sub-threads.
- **Conversation cards.** Each step is a card in its kind's color. A run of tool calls is one card of tiles, each opening its detail or a window of its own, and a turn ends with its **Modified files** and a **Turn** card. Marks beside the scroll bar show your messages, waiting prompts and errors; `F3` and `F4` go to the previous and next message, and `Ctrl/Cmd+F3` and `Ctrl/Cmd+F4` to the first and the latest.
- **Quieter in-between steps.** Tool calls, thinking, subagents and the other steps on the way to an answer lose their card's tint and outline, keeping the stripe, until you point at one, so your messages and Claude's answers stand out. Settings → Appearance → **Quieter in-between steps** turns it off.
- **Give Claude a task list.** Newer models keep track of their work without a written list, so the Tasks page showed only the plan. Settings → Claude Code → **Give Claude a task list**, on unless you turn it off, gives every model one, at the cost of a little room in the context. With no tasks, the status line under the composer says *Following the plan* once a plan is approved.
- **Renaming a folder group**, for folders with the same name in different places: **Rename…** in the group's menu, and **Reset name** to go back to the folder's name.
- **Using usage credits.** Once a limit is used up and your plan's usage credits pay for Claude, a notice under the header says which limit, when it resets, and what you've spent this month, with **Manage on claude.ai**. It stays until they stop paying, and an OS notification tells you when they start.
- **Tabs that don't use usage credits.** Untick **Use usage credits** in a tab's menu, or turn off Settings → Usage → **Use usage credits once a limit is used up** for every tab, and once a limit is used up the tab stops and waits for it to reset instead of spending them, so credits go only to the work that matters. A message you send meanwhile waits for the reset, with **Send now** to send it anyway.
- **The prompt cache's timer.** *Cache 52m* beside the context says how long your next message can read the conversation from Claude's cache, and turns amber in its last three minutes. Settings → Appearance → **Show when the prompt cache expires** turns it off.
- **Games with Claudette.** Tic-tac-toe, Connect Four and hangman, from her right-click menu, the command palette, or **Play a game while you wait** when a usage limit stops a task. Settings → Appearance → **Games with Claudette** turns them off.
- **Clear usage history on every machine…** in Settings → Usage, while **Share usage with my other machines** is on.
- **User guide**, under **Settings** at the foot of the sidebar and at the foot of the Settings sidebar, opens Claudette's guide in your browser.
- A turn's changes to a file open in your diff tool too, from the diff window's **Open in diff tool**, and its menu has **Open in the OS's app**.

### Changed

- **Also restore unpinned tabs** (Settings → Sessions) is on for new installs, so every tab comes back at launch. Settings saved by 0.2.0 or earlier keep it off until it's turned on, or Sessions' **Reset to defaults** is used.
- **Share usage with my other machines** shares the turns of tabs that sync to the session library too. The Usage window's **Projects** adds up what every machine spent on a project, its tabs and the busiest tabs list another machine's as *Fix the loader · Laptop*, and an API-key account's spend and its warnings count its turns on your other machines under an account of the same name.
- The Usage window's **Projects** counts a project as one row in any of its folders, such as a worktree or another clone.
- Tested with Claude Code 2.1.300. A tab Claude Code refuses to start because of an organization's configuration says so in words.
- The license's copyright holder is dotBunny Inc.

### Fixed

- A tab's tokens and estimated cost, and the Usage window's, no longer count every earlier turn of the session again with each turn, which made a long session look far bigger than it was. Turns recorded by 0.2.0 or earlier keep the counts they had.
- **On pace to hit the session limit** under the header keeps up with the projection, and goes once you're no longer on pace to hit it, rather than repeating the time it was first worked out from.
- Right-click menus no longer run off the bottom of some displays, and Claudette's own menu stays inside the window.
- The folder trust question fits in the window when a folder's configuration lists a lot.
- The spacing of thinking and the height of lines in the conversation.
- In a long conversation, the scroll bar keeps its size and place as you scroll, rather than changing as each message comes into view.
- The tab info card's **Project** row no longer ends a sentence with two full stops when the project's engine wasn't found.
- In a narrow conversation, as beside the side panel, the line saying what Claude is doing no longer runs over the side panel: a long command trims to fit. The composer's choices wrap onto another line, and the folder and status under it trim, rather than running past its edge.
- **New tab** (`Ctrl/Cmd+T`) puts the cursor in its search box, so you can type a folder's name straight away.

## [0.2.0] - 2026-10-14

### Added

- **Threads.** Make a tab a thread from its **Thread ▸** menu, and its Claude hands work to the other tabs in its project, its sub-threads, and gets one report back when they've finished. You approve each message first, unless **Ask before sending to sub-threads** is off, and answer the sub-threads' prompts from a strip over the thread's composer.
- **Following a plan.** The Tasks page shows Claude's plan while it writes it in Plan mode, keeps every version with **Changes from** the one before, and can take a reply as the plan. Each task says who worked on it, how long it took, and the files and tokens it used. A tab's row shows how far its tasks have got (`3/7`), and a turn that ends with tasks left offers **Continue**.
- **Sessions that know each other.** Each tab's session goes by the tab's name in Claude Code (Settings → General → **Name each tab's session in Claude Code after the tab**), so other sessions can message it by name. `@` in the composer names another tab, another Claude Code session on this machine, or one of the tab's subagents. The tab's menu has **Copy session ID**, and its info card **Reached as**.
- **Claudette on the composer**, who climbs onto the message box and reacts to what the tab is doing. Settings → Appearance → **Show Claudette on the composer** turns her off.
- **Claude Docs.** A doc Claude writes on claude.ai with the Docs connector gets a link on its card, a row with **Open** at the end of the turn, and a place on the Tasks page.
- **Images from Claude's tools** show under their cards: an image Claude read, a PDF's pages, a notebook's figures, an MCP tool's screenshot. A click opens one full size, with **Copy image**, **Save as…** and **Open**.
- **Prompt suggestions.** Settings → Claude Code → **Suggest my next prompt** shows what Claude Code thinks you'll send next in the empty composer; `Tab` takes it.
- **Keep this computer awake** in Settings → General, while Claude has work to finish or whenever Claudette is running, and the cup in the header that turns it on and off.
- The button at the header's right shows and hides the side panel, and its pages can be dragged into the order you like (or **Move left**, **Move right** and **Reset order**).
- History has a chip for each recent project, to list only its sessions, and a group's menu has **History for this folder**.
- The Processes page collapses a process's children, counting them and their CPU and memory on its row, with **Expand all** and **Collapse all**.

### Changed

- Claudette moved to [dotBunny/Claudette](https://github.com/dotBunny/Claudette), and its packages are dotBunny's. 0.1.0 announces 0.2.0 but can't install it, so install 0.2.0 by hand; on Windows it installs beside 0.1.0.

### Fixed

- A link clicked in a reply opens again.
- When the last tab in a worktree reached through a symbolic link closes, Claudette offers to remove the worktree.

### Security

- A reply's images no longer load by themselves. An image from the web, or a file outside the tab's folder, waits for **Load image**, so a reply can't send anything out in an image's address, and an image on a network path never loads.

## [0.1.0] - 2026-10-08

The first public release.

### Added

- **Tabs**, each a Claude Code session running the real `claude` with your settings, `CLAUDE.md`, MCP servers and hooks, grouped by folder in a sidebar with each group's color. Status icons, pins, marks and the reviewed badge, tabs restored at launch, the command palette (`Ctrl/Cmd+Shift+P`) and the next tab waiting for you (`Ctrl/Cmd+J`).
- **Worktree tabs**, each in a git worktree and branch of its own, with setup actions for each new worktree and cleaning up the ones you've merged; and extra folders a tab's Claude may also use.
- **The conversation**: replies in Markdown, tool calls with their diffs and output, subagents, thinking, hook runs, find (`Ctrl/Cmd+F`), exporting to Markdown or a web page, and going back to an earlier message with **Edit and resend…**, **Branch from here** and **Restore files to before this…**. The working line says what Claude is doing, and a chip counts the tasks it keeps running.
- **The composer**: `/` commands and `@` files, attachments and large pastes, earlier prompts, drafts and the stash (`Ctrl/Cmd+S`), messages queued while Claude works, quick suffixes, the model, effort and permission mode, **Ultracode** and output styles, and check-ins on long turns.
- **Permission prompts** as cards, with **Always allow** rules you can edit, questions, plans to approve, MCP servers' forms, and a question before using a new folder's own configuration.
- **Changes to review**: the Changed files page, a diff view, ticking files reviewed, reverting a change or a whole file, and your own diff tool.
- **Usage**: the session and weekly meters with a trendline and a projection, a detailed header with charts, alerts, a Usage window with each project's tokens and cost, the context window and **Compact now**, continuing a task when a usage limit resets, and usage shared between your machines.
- **History** of every session, and a session library in a synced folder that lets another machine continue the tabs you choose, with Claudette's settings synced too.
- The **agent map** of Claude's subagents, the **Tasks** page, the **MCP** page, the **Processes** page and a **scratch pad** for each project.
- **Project tools** for Unreal Engine, Unity and Godot, your own actions and links in `claudette.json`, and Perforce logins kept fresh, with each tab's changelist.
- **The Claude app**: connect a tab to use it from your phone or claude.ai. Notifications, the Dock or taskbar badge, starting at login, and Claude's service status in the header.
- **Updates** to Claude Code, and to Claudette itself.
- The Standard and Claude styles, in light and dark, with compact density, zoom, reduced motion and screen readers' announcements.
- Packages for Windows (MSIX) and macOS (`.dmg`), for x64 and Arm.

[Unreleased]: https://github.com/dotBunny/Claudette/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/dotBunny/Claudette/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/dotBunny/Claudette/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/dotBunny/Claudette/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/dotBunny/Claudette/releases/tag/v0.1.0
