# Claudette User Guide

The plan for Claudette's user guide, the website at https://claudette.dotbunny.com that shows people how to use every part of Claudette. [DESIGN.md](DESIGN.md) says what Claudette does; this file says how the guide is built, how a page is written, how its pictures are taken, which page describes each part of Claudette, and how `CHANGELOG.md` is kept. `§n` still means a section of DESIGN.md, whose [User guide](DESIGN.md#user-guide) section has the decisions behind the site.

Planned 2026-10-15, not started.

## 1. The site

- **Where it lives.** In `docs/` on `main`, with `CHANGELOG.md` from the repository's root. A workflow of ours builds the site and publishes it to GitHub Pages (the repository's Settings → Pages → Source: **GitHub Actions**).
  - `docs/CNAME` holds the domain, `claudette.dotbunny.com`. GitHub wrote it when the domain was set, and deleting it unsets the domain. The domain's DNS points at GitHub Pages, and the site answers over HTTPS (with a 404 until there's a page).
  - Everything in `docs/` is published, the README's screenshots included, and so is the changelog. Nothing else outside it is, so this file isn't.
- **How it's built.** With Jekyll, through the `github-pages` gem (`docs/Gemfile`), which pins Jekyll and the plugins to the versions GitHub Pages uses. The theme is [Just the Docs](https://just-the-docs.com), as a `remote_theme` pinned to a release's tag. It gives a sidebar from each page's front matter (`nav_order`, `parent`, `has_children`), search built with the site, callouts, and an **Edit this page on GitHub** link.
  - **Why a workflow.** GitHub Pages' own build from a branch would do for the written pages, with no workflow to keep. But it reads only `docs/` and runs only on a push, and two pages need more ([Downloads and the changelog](#5-downloads-and-the-changelog)): the Changelog page is made from `CHANGELOG.md` at the root, and the Downloads page from the releases, so it's rebuilt whenever one is published.
  - `docs/_config.yml`: the title; `url: https://claudette.dotbunny.com` and no `baseurl`, since the site is the domain's root; the theme; the plugins (`jekyll-remote-theme`, `jekyll-seo-tag` and `jekyll-sitemap`, all on GitHub Pages' list); search; links to the repository and its Releases; and the edit link, to `docs/` on `main`.
  - **Light and dark.** The site follows the reader's system, as Claudette does by default. Just the Docs has a light scheme and a dark one but nothing that follows the system, so `_includes/head_custom.html` calls `jtd.setTheme` from `prefers-color-scheme`, and again when that changes. The page has no switch of its own, so the pictures can follow the same setting ([Pictures](#3-pictures)).
  - **The icon and favicon:** `packaging/icon/build-icons.mjs` writes them to `docs/assets/` too, so they stay the app's ([Packaging and signing](DESIGN.md#packaging-and-signing)). The site in Claudette's own colors, as a custom scheme in `_sass/color_schemes/`, can come later.
- **The workflow**, `.github/workflows/docs.yml`.
  - It runs on a push to `main` that changes `docs/` or `CHANGELOG.md`, on a release being published, edited, unpublished or deleted, and from **Run workflow**. A pull request that changes either gets the same build and checks, without publishing.
  - **Building.** `docs/_scripts/prepare.sh` writes the two files the generated pages read, which `.gitignore` keeps out of the repository ([Downloads and the changelog](#5-downloads-and-the-changelog)): `docs/_data/releases.json`, from the GitHub API with the workflow's token, and `docs/_includes/changelog.md`, from `CHANGELOG.md`. Then Jekyll builds the site. If the API can't be read, or `CHANGELOG.md` has no version in it, the build fails rather than publish an empty page.
  - **Checks.** html-proofer runs over the built site, for internal links only. A broken link, a missing picture or a picture with no alt text fails the run instead of reaching the site.
  - **Publishing**, on `main` and for releases: `actions/upload-pages-artifact` and `actions/deploy-pages`, in the `github-pages` environment, one at a time. The workflow can read the repository and write to Pages, nothing else. Its actions are pinned to commits, as `package.yml`'s are.
- **Previewing it.** With Ruby installed, run `docs/_scripts/prepare.sh` (it needs `gh` signed in, or `GH_TOKEN`), then `bundle exec jekyll serve` in `docs/`. Without the first step, the written pages are all there, and the two generated pages link to GitHub instead.
- **Chapters and pages.** Each chapter in [The chapters](#4-the-chapters) is a folder of pages with an `index.md`, which says what the chapter covers and lists its pages. A page's file is its title in lowercase with hyphens: **Worktree tabs** is `docs/tabs/worktree-tabs.md`. Chapters sit in the sidebar in the map's order.

## 2. Writing a page

- **Who it's for.** People using Claudette, not working on it: what the part is for, how to do each thing (in numbered steps where there are steps), the settings that change it and its shortcuts. No class names, protocol or other internals.
- **The app's own words.** In bold, exactly as the app shows them: Settings → Appearance → **Style**, **Always allow**. Where DESIGN.md and the app disagree, the app wins, and DESIGN.md is corrected in the same change. Shortcuts are written as `Ctrl/Cmd+J`; the Reference chapter lists both.
- **One home for each part.** Each part of Claudette is explained on one page, the one the map names. Other pages say what it is in a sentence and link to it. A setting is explained on its feature's page, and Settings → **Every setting** links there.
- **The shape of a page.**
  1. What the part is for, in a sentence or two, and a picture of it.
  2. How to do each thing with it, a heading each.
  3. **Settings**: each setting that changes it, its default, and a link to its Settings page.
  4. **Shortcuts**, if it has any.
  5. **See also**, for the pages it's easily confused with.
- **Windows, macOS and Linux.** A difference is written where it applies: "On macOS, …". Reference → **Windows, macOS and Linux** collects them.
- **Claude Code's part.** What Claude Code itself does (permission modes, hooks, MCP servers, `CLAUDE.md`, Plan mode) links to [Claude Code's docs](https://code.claude.com/docs) rather than being explained again. The page says what Claudette shows of it.
- **Only what's built.** Only what's built (✅ in DESIGN.md) has a page. A feature that's only designed gets its page when it's built, and a page that mentions one doesn't. Something built but still to verify on a real machine is covered like the rest.
- **Ahead of a release.** The site is published from `main`, so it can describe things no release has yet. A section about one starts with a *New in the next release* note (`{% include unreleased.html %}`), which publishing the release removes ([Downloads and the changelog](#5-downloads-and-the-changelog), "Releasing"). The change is under **Unreleased** in `CHANGELOG.md` meanwhile.

## 3. Pictures

- They're of the real app running the demo (`tools/Claudette.Demo`), never anyone's account or projects. The UI tests' headless renders won't do: they draw text about twice as wide as a screen does.
- `tools/Claudette.Demo/screenshots.ps1` takes the README's pictures, clicking through UI Automation and capturing with `PrintWindow`, into `docs/screenshots/`. The guide's come from a sibling, `guide-screenshots.ps1`, with the helpers both use moved into a module. Each shot is a name, the steps to it, and the element to crop to, from its UI Automation bounds with a margin: a picture of the part a page describes reads better than the whole window. They go in `docs/screenshots/guide/<chapter>/<name>-<light|dark>.png`.
- In the Standard style, light and dark. The Claude style shows only on the pages about it: every picture in all four looks would double what the pictures add to the repository, for little (the README's 33 are 6.8 MB).
- A page shows the picture that matches the reader's theme, through an include, `{% include shot.html name="…" alt="…" %}`, which writes a `<picture>` with a `prefers-color-scheme: dark` source. Every picture has alt text that says what it shows, as the README's do.
- **What the demo doesn't show yet.** The demo grows what a chapter needs, or the script makes it happen with `fake-claude`'s prompts: a waiting permission (`ASK_PERMISSION`), a usage limit (`LIMIT`), a process for the process monitor (`SPAWN`), a prompt suggestion (`SUGGEST`), a Claude Doc (`DOC`), and the sign-in screen (`FAKE_CLAUDE_LOGGED_IN=0`). A question, a plan to approve and an MCP server's form need `fake-claude` to learn them, as the mock API knows `ASK_QUESTION` and `EXIT_PLAN`. Perforce has no fake outside the tests, so its page can do without pictures.
- The script runs on Windows. What only macOS or Linux shows, such as the menu bar and the Dock icon's badge and animation, is captured by hand and named `-macos`, or described without a picture.

## 4. The chapters

Each chapter's table lists its pages in order, what each covers, and the DESIGN.md sections it's written from. A page is written from those sections, then checked against the running app.

### Home (`docs/index.md`)

What Claudette is, a picture of the window, a download button for the latest release with what's new in it, where to start. From [§1](DESIGN.md#1-goals) and the README.

### Downloads (`docs/downloads.md`)

One page, second in the sidebar: the latest release's packages, the next version when a pre-release is newer, and every earlier release. Made from the releases ([Downloads and the changelog](#5-downloads-and-the-changelog)).

### Getting started (`getting-started/`)

| Page | Covers | From |
|---|---|---|
| What you need | Windows (x64 and arm64), macOS 12 or later, Linux from source and less polished; Claude Code installed first, at its minimum version. | [§1](DESIGN.md#1-goals), [§2](DESIGN.md#2-platform--tech-stack) |
| Installing | Claude Code first. Which package to take from the Downloads page. The MSIX on Windows, which shares Claude Code's configuration with the terminal's `claude`. The `.dmg` on macOS, moving Claudette to Applications, and the privacy prompts that name Claudette when Claude reads a project in Documents or Downloads. | [Packaging and signing](DESIGN.md#packaging-and-signing) |
| The first launch | *Looking for Claude Code…*, the setup screen (**Update now**, **Check again**, **Choose claude…**, **Install instructions**), Claude Code's minimum version. | [§2](DESIGN.md#2-platform--tech-stack), [§12](DESIGN.md#12-claude-code-updates) |
| Signing in | The sign-in screen, finishing in the browser, **Enter a code instead**, **More options** (a Console account, SSO), **I've already signed in**. What can go wrong is in Troubleshooting. | [§11](DESIGN.md#11-sign-in) |
| Your first tab | The new tab picker, choosing a folder, the folder trust question, sending a message, **Stop**. | [Opening a tab](DESIGN.md#opening-a-tab), [Folder trust](DESIGN.md#folder-trust), [Composer](DESIGN.md#composer) |
| A tour of the window | The header (the usage meters, the cup, the service status dot, the account), the sidebar, the conversation, the side panel and the composer; on macOS the menu bar. Each part links to its chapter. | [§3](DESIGN.md#3-main-window) |
| How Claudette uses Claude Code | Each tab runs the real `claude`, so your `CLAUDE.md`, settings, hooks, MCP servers, skills and plugins load as in a terminal; a folder's own configuration once it's trusted; where Claude Code keeps sessions; starting Claudette from a terminal inside Claude Code; the helper `claude` in the process list. | [§13](DESIGN.md#13-architecture), [Integration with Claude Code](DESIGN.md#integration-with-claude-code) |
| Running from source | Building and running Claudette from a checkout, running from a copy, **New build ready** and restarting into new builds, `CLAUDETTE_RUN_IN_PLACE`, the commit in the version, and trying Claudette on the demo. | [Working on Claudette](DESIGN.md#working-on-claudette), [Version](DESIGN.md#version) |

### Tabs and sessions (`tabs/`)

| Page | Covers | From |
|---|---|---|
| Opening tabs | The new tab picker (search, **Favorites**, **Recent folders**, **Browse…**, **Open from History…**, `1`–`9`, its right-click menu), a group's **+**, dropping a folder on the sidebar, **File → Open Recent** and the Dock menu on macOS, the jump list on Windows, `--folder`, one Claudette at a time. | [Opening a tab](DESIGN.md#opening-a-tab) |
| The sidebar | A tab's row: the status icons, the second line (*Needs your input*, *Possibly stuck*, the task in progress), the badges (pin, sync, phone, gear, running tasks, task progress, branch, changelist, account, context ring), the info card on hover, resizing, the rail and narrow windows. | [Sidebar](DESIGN.md#sidebar), [§4](DESIGN.md#4-tabs--sessions) |
| Folder groups | Groups and their colors, **Rename…** and **Change color…**, collapsing, dragging tabs and groups, the group's menu (**New tab here**, **History for this folder**, **Account ▸**, **Close group**). | [§4](DESIGN.md#4-tabs--sessions) |
| Names, marks and pins | Claude Code's titles and the folder name before one, **Rename** and **Reset name**, **Name each tab's session in Claude Code after the tab**, **Mark ▸** and **Clear mark**, the reviewed icon over a mark, **Pin tab**. | [§4](DESIGN.md#4-tabs--sessions), [§13](DESIGN.md#13-architecture) |
| The tab's menu | Every item, each with a line and a link to its page; **Copy session ID**, **Duplicate tab**, **Move up** and **Move down** are explained here. | [Sidebar](DESIGN.md#sidebar) |
| The tab info card | Hovering a row or clicking **ⓘ**, and each row the card can show: folder and branch, model, session and **Reached as**, tokens, context, Perforce, project, Claude app, agents, running tasks, tasks, usage limit, worktree, account, extra folders. | [§4](DESIGN.md#4-tabs--sessions) |
| Tab settings | **Tab settings…**: the overrides (model, effort, permission mode, process monitor, **Auto-continue**, check-ins), the dot beside it, **Use defaults**; and **Sync to other machines**, **Connect to the Claude app** and **Extra folders**, which aren't overrides. | [Per-tab overrides](DESIGN.md#per-tab-overrides), [§4](DESIGN.md#4-tabs--sessions) |
| The side panel | Its pages and when each shows, opening and closing it, the page tabs' busy dots and **⋯**, **Move left**, **Move right** and **Reset order**, resizing it. | [§3](DESIGN.md#3-main-window) |
| Split panes | **Open beside**, dragging a row onto a pane, the focused pane and what follows it, resizing and closing panes. | [Split panes](DESIGN.md#split-panes) |
| The command palette | `Ctrl/Cmd+Shift+P`, its sections and filtering, and **Go to the next tab waiting for you** (`Ctrl/Cmd+J`). | [Command palette](DESIGN.md#command-palette) |
| Worktree tabs | **New tab in a worktree**, how a worktree tab shows, duplicating and branching in one, closing its last tab (**Remove**, **Keep**, **Remove and discard**), Settings → General → **Worktrees** and **Clean up now**. Setup actions for a new worktree are in Project tools. | [Worktree tabs](DESIGN.md#worktree-tabs), [Cleaning up worktrees](DESIGN.md#cleaning-up-worktrees) |
| Processes | The process monitor: turning it on, the composer bar's summary, the header's total, the **Processes** page and its menu, stopping a process. | [Process monitor](DESIGN.md#process-monitor) |
| MCP servers | The **MCP** page: each server's status, **Reconnect**, **Turn off**, **Sign in** and **Sign out**. A server's form is in Permissions and prompts. | [MCP servers](DESIGN.md#mcp-servers) |
| Closing and restoring | Closing a working tab and its confirmations (**Close and stop them**, **Close, leave running**), pinned tabs, restoring at launch and **Also restore unpinned tabs**, tabs that start when selected, a folder that's gone, a session Claude Code deleted, **Restart** after `claude` stops. | [§4](DESIGN.md#4-tabs--sessions), [Restore on launch](DESIGN.md#restore-on-launch), [§13](DESIGN.md#13-architecture) |

### The conversation (`conversation/`)

| Page | Covers | From |
|---|---|---|
| Reading the conversation | The cards and what their colors mean in each style, times, following new output and **Jump to latest**, **Quieter in-between steps**, **Full-width conversation**. | [§5](DESIGN.md#5-conversation-view), [Conversation cards](DESIGN.md#conversation-cards), [Copy and times](DESIGN.md#copy-and-times) |
| Tool calls | The **Tool calls** card and its tiles, a tile's detail and **Show all**, Edit and Write diffs, Bash's output and git chips, a call in a window of its own. | [Conversation cards](DESIGN.md#conversation-cards), [§5](DESIGN.md#5-conversation-view) |
| Thinking, notes and hooks | Thinking (**Show thinking expanded**, **Collapse all thinking**), notes, warnings and errors, API retries, hook runs and **Show every hook run in the conversation**. | [§5](DESIGN.md#5-conversation-view), [Hook runs](DESIGN.md#hook-runs), [Notices](DESIGN.md#notices) |
| Subagents | A subagent's card, its own cards and report, subagents in the background. The agent map is in Plans, tasks and agents. | [§5](DESIGN.md#5-conversation-view) |
| The end of a turn | The **Modified files** and **Turn** cards, the **Tasks** card, the Claude Docs a turn wrote. | [Conversation cards](DESIGN.md#conversation-cards), [Changes per turn](DESIGN.md#changes-per-turn) |
| Finding your way | The scroll bar's marks, `F3` and `F4`, `Ctrl/Cmd+F3` and `Ctrl/Cmd+F4`, find (`Ctrl/Cmd+F`). | [Conversation cards](DESIGN.md#conversation-cards), [Find](DESIGN.md#find) |
| Copying and quoting | **Copy**, **Copy message**, **Quote in reply**, **Quote message**, a code block's header (**Wrap lines**, **Copy**). | [Copy and times](DESIGN.md#copy-and-times) |
| The scratch pad | One pad per project, **Add to scratch pad**, **Copy all**, **Clear…**, how it syncs and its conflicts (**Keep both**, **Use theirs**, **Keep mine**). | [Scratch pad](DESIGN.md#scratch-pad) |
| Exporting | **Export conversation…** to Markdown or a web page, and what's left out. | [Export](DESIGN.md#export) |
| Rewind and branch | **Edit and resend…**, **Branch from here**, **Restore files to before this…** and **Keep copies of files Claude changes**, **Duplicate tab**. | [Rewind and branch](DESIGN.md#rewind-and-branch) |
| Images | Images from Claude's tools, images in its replies and **Load image**, the image window (**Copy image**, **Save as…**, **Open**). | [Images from Claude's tools](DESIGN.md#images-from-claudes-tools), [Images in Claude's replies](DESIGN.md#images-in-claudes-replies) |
| Files and links in replies | Clicking a file, a line, a folder or a web link; several files with one name; a file that isn't there; what's never opened. | [Files named in Claude's replies](DESIGN.md#files-named-in-claudes-replies) |
| The working line and running tasks | The line above the composer, **Show fun words while Claude works** and **Show what Claude is doing while it works**; the running tasks chip, its list and **Stop**. | [Working line](DESIGN.md#working-line), [Running tasks](DESIGN.md#running-tasks) |

### The composer (`composer/`)

| Page | Covers | From |
|---|---|---|
| Sending and stopping | **Send with**, **Stop** and `Esc`, messages sent while Claude works: into the turn, or queued with **Send now** and **Cancel**. | [Composer](DESIGN.md#composer), [Queued messages](DESIGN.md#queued-messages) |
| The composer bar | The folder, model, effort and mode on the left; processes, **Agents**, running tasks, **Files**, context, the prompt cache, tokens and **ⓘ** on the right; the status line under the box. Each links to its page. | [Composer](DESIGN.md#composer) |
| Slash commands and mentions | `/` commands; `@` files and folders; `@` sessions, tabs and agents, which Threads explains. | [Autocomplete](DESIGN.md#autocomplete) |
| Attachments and large pastes | Dropping, pasting and the paper clip; images and their limits; files and folders as `@` mentions; large pastes as a chip, and `Ctrl/Cmd+Shift+V`; long messages shown short. | [Attachments](DESIGN.md#attachments) |
| Earlier prompts, drafts and the stash | `Up` and `Down`, drafts kept when Claudette closes, **Stash the message** (`Ctrl/Cmd+S`) and **Stashed ▾**. | [Composer](DESIGN.md#composer), [Drafts and the stash](DESIGN.md#drafts-and-the-stash) |
| Quick suffixes | **Suffixes ▾** (`Ctrl/Cmd+Shift+S`), the chips, **Keep on this tab**, each suffix's shortcut, Settings → Quick suffixes and the built-in suffixes. | [Quick suffixes](DESIGN.md#quick-suffixes) |
| Model, effort and output style | Switching the model and its confirmation, effort, **Ultracode**, **Output style**, **Fallback model**. The permission mode is in Permissions and prompts. | [Model & effort](DESIGN.md#model--effort) |
| Prompt suggestions | **Suggest my next prompt**, and taking one with `Tab` or `→`. | [Prompt suggestions](DESIGN.md#prompt-suggestions) |
| Check-ins on long turns | When a check-in is sent, its countdown with **Send now** and **Don't check in**, *Possibly stuck*, Settings → Check-ins and a tab's own. | [Check-ins on long turns](DESIGN.md#check-ins-on-long-turns) |

### Permissions and prompts (`permissions/`)

| Page | Covers | From |
|---|---|---|
| Permission cards | What a card shows, **Allow**, **Always allow…** and editing its rule, **Allow for this session only**, **Allow all edits this session**, **Deny** and **Deny with a message…**, several prompts waiting, a subagent's prompt, answering from the keyboard. | [§7](DESIGN.md#7-permission-prompts) |
| Permission modes | The mode picker, when Auto isn't offered, Bypass permissions and its warning, the mode a tab starts in. | [§7](DESIGN.md#7-permission-prompts), [Starting mode](DESIGN.md#starting-mode) |
| Questions | The question card, **Send answers** and **Dismiss**. | [§7](DESIGN.md#7-permission-prompts) |
| Approving a plan | The plan card, **Approve, auto mode** and the other approvals, **Keep planning…**, versions and **Changes from v1**. Following the plan is in Plans, tasks and agents. | [§7](DESIGN.md#7-permission-prompts), [Tasks](DESIGN.md#tasks) |
| MCP servers asking for input | A server's form, **Send**, **Open link** and **Done**, **Decline**. | [§7](DESIGN.md#7-permission-prompts) |
| Folder trust | **Trust this folder?** and what it lists, **Start without it**, the tab menu's **Trust this folder**, **Ask before using a new folder's own configuration**. | [Folder trust](DESIGN.md#folder-trust) |

### Plans, tasks and agents (`plans/`)

| Page | Covers | From |
|---|---|---|
| The Tasks page | The task list, the plan and its versions, **Use as the plan** and **Show as the plan**, task rows in the conversation, **Continue**, progress on the row and the status line, **Give Claude a task list**. | [Tasks](DESIGN.md#tasks) |
| Claude Docs | Docs Claude writes with claude.ai's Docs connector: the link on the tile, the turn's rows, the Tasks page's **Docs**. | [Claude Docs](DESIGN.md#claude-docs) |
| The agent map | The **Agents** page and its window, a subagent's details, stopping one, **Summarize subagents' progress**. | [Agent map](DESIGN.md#agent-map) |

### Threads (`threads/`)

| Page | Covers | From |
|---|---|---|
| What threads are | A thread hands work to sub-threads in its folder group; what Claude is told; prompts stay yours; what threads don't do. | [Threads](DESIGN.md#threads) |
| Making a thread | The **Thread ▸** menu (**Make this tab a thread**, **New sub-thread** and **New sub-thread in a worktree**, **Assign to thread ▸**, **Detach from thread**, **Stop being a thread**), threads in the sidebar, **Show with its sub-threads**. | [Threads](DESIGN.md#threads), [Split panes](DESIGN.md#split-panes) |
| Sending work and getting reports | **Ask before sending to sub-threads** and its question, *From the thread*, the *From the sub-threads* report and how a sub-thread's work can end. | [Threads](DESIGN.md#threads) |
| Sub-threads waiting on you | The strip over the thread's composer: **Allow**, **Deny**, **Go to tab**. | [Threads](DESIGN.md#threads) |
| Reaching other sessions | A session's name in Claude Code, **Reached as**, `@` sessions and tabs, **Copy session ID**, *From the session*. | [§13](DESIGN.md#13-architecture), [Autocomplete](DESIGN.md#autocomplete) |

### Reviewing changes (`changes/`)

| Page | Covers | From |
|---|---|---|
| Changed files | The **Changed files** page and the **Files** count, the line counts, **Working tree vs HEAD**, a file whose earlier contents aren't known. | [§8](DESIGN.md#8-file-changes--diff-view) |
| The diff view | Side by side or inline, **Whole file**, the file's actions, **Open** and **Open in the OS's app**. | [§8](DESIGN.md#8-file-changes--diff-view) |
| Reviewed files | Ticking files, what unticks them, the diff view's **Reviewed**, the row's reviewed icon. | [§8](DESIGN.md#8-file-changes--diff-view) |
| Reverting | **Revert** on a change, **Revert file**, and what can't be reverted. | [Reverting](DESIGN.md#reverting) |
| Changes per turn | A turn's **Modified files** and its read-only diffs. | [Changes per turn](DESIGN.md#changes-per-turn) |
| External diff tools | Settings → Diff tool: **Built-in**, the presets, a custom command and its placeholders, **Test**. | [External diff tool](DESIGN.md#external-diff-tool) |

### Project files (`project-files/`)

| Page | Covers | From |
|---|---|---|
| Opening project files | The **Project files** button, **Open project files**, **Go to file…** (`Ctrl/Cmd+P`), its sidebar row, panes, and what it doesn't do (no language features, terminal or source control). | [Project files](DESIGN.md#project-files) |
| The explorer | The tree, dimmed and linked entries, following the disk, its menus, **Show only changed files**, its side and width, hiding it. | [Project files](DESIGN.md#project-files) |
| Finding a file by name | The find box, `name:42`, its keys and messages. | [Project files](DESIGN.md#project-files) |
| File tabs | Preview and kept tabs, **Pin**, the **Close…** commands, moving between files. | [Project files](DESIGN.md#project-files) |
| Editing | The editor, **Wrap long lines**, languages and colors, the chip (line endings, indentation, encoding), files that open read-only or not at all. | [Project files](DESIGN.md#project-files) |
| Saving | Saving and **Save all**, unsaved edits kept on disk, the questions on closing, a file that changes on disk and its banners. | [Project files](DESIGN.md#project-files) |
| Claude's changes in the editor | The marks in the explorer and the gutter, the peek and **Revert**, `F3` and `F4`, the mode bar, **Reviewed**, **Against git HEAD**. | [Project files](DESIGN.md#project-files) |
| Opening files from the conversation | Which files open where, at the line named, in which Files tab, and **Show a file clicked in a conversation**. | [Project files](DESIGN.md#project-files), [Files named in Claude's replies](DESIGN.md#files-named-in-claudes-replies) |

### Usage and limits (`usage/`)

| Page | Covers | From |
|---|---|---|
| The usage header | The session and weekly meters, their colors and the warning levels, the trendline and the line under it, the model meters and reading them from `/usage`, a reading's "as of" time. | [Header meters](DESIGN.md#header-meters), [Burn trendline](DESIGN.md#burn-trendline) |
| The detailed header | **Detailed usage header**: the charts, **Burn rate**, **Time to limit**, **Busiest tabs**. | [Detailed header](DESIGN.md#detailed-header) |
| The Usage window | Tabs in this session window, **Projects** and its period, **Accounts**, past sessions and weeks. | [Burn trendline](DESIGN.md#burn-trendline), [Usage history](DESIGN.md#usage-history) |
| A tab's tokens | The composer bar's token count and its breakdown. | [§4](DESIGN.md#4-tabs--sessions) |
| Context and compacting | The context indicator, the row's ring, the breakdown, **Compact now**, *Conversation compacted*. | [Per-tab context](DESIGN.md#per-tab-context) |
| The prompt cache | The **Cache** chip, its tooltip, **Show when the prompt cache expires**. | [Prompt cache](DESIGN.md#prompt-cache) |
| Alerts | When alerts come, where they show, turning them off. | [Alerts](DESIGN.md#alerts) |
| When a limit stops a task | The limit bar, continuing when the limit resets, **Don't continue**, **Continue when it resets**, why it won't continue by itself, a tab's **Auto-continue**, **Play a game while you wait**. | [Continuing after a limit resets](DESIGN.md#continuing-after-a-limit-resets) |
| Usage history | What's kept and for how long, **Keep usage history**, **Clear usage history…**. | [Usage history](DESIGN.md#usage-history) |
| Sharing usage across machines | **Share usage with my other machines**, what it adds, **Clear usage history on every machine…**. | [Sharing across machines](DESIGN.md#sharing-across-machines) |

### Accounts (`accounts/`)

| Page | Covers | From |
|---|---|---|
| Your Claude sign-in | The **Default** account, the header's account menu, **Plan and billing** or **Console billing**, **Sign out…** and what it stops. | [§11](DESIGN.md#11-sign-in), [Accounts](DESIGN.md#accounts) |
| API-key accounts | Settings → Accounts: **Add an API key**, where the key is kept, **Change key…**, **Remove…**; on Linux, `secret-tool`. | [Accounts](DESIGN.md#accounts) |
| A folder's account | A group's **Account ▸**, **New folders use**, the account tags. | [Accounts](DESIGN.md#accounts) |
| Moving a tab to another account | A tab's **Account ▸**, and what moving it costs. | [Accounts](DESIGN.md#accounts) |
| Spend and its warnings | The header's spend, the warnings, the Usage window's **Accounts**, and what an API-key account doesn't have (plan meters, the Claude app). | [Accounts](DESIGN.md#accounts) |

### History and syncing (`history/`)

| Page | Covers | From |
|---|---|---|
| History | `Ctrl/Cmd+Shift+H`, the project chips, searching and **Search Claude's replies too**, opening a session, worktree sessions. | [History](DESIGN.md#history) |
| The session library | What it is, its folder and **Move library…**, a cloud-synced folder and its warning, **Name for this machine**, **Keep library sessions for**, keeping sessions past Claude Code's 30 days. | [Session library](DESIGN.md#session-library-sync-across-machines) |
| Syncing a tab | **Sync to other machines**, **Sync new tabs to the session library**, **Sync now** and its notes. | [Session library](DESIGN.md#session-library-sync-across-machines) |
| Picking up on another machine | Finding the folder, the warning when the code may differ, one machine at a time (**Open a copy**, **Take over**, a read-only tab), conflict copies. | [Session library](DESIGN.md#session-library-sync-across-machines) |
| Settings sync | **Sync Claudette's settings through the library**, the first choice it asks, what syncs and what stays on this machine. | [Settings sync](DESIGN.md#settings-sync-optional) |

### Project tools (`project-tools/`)

| Page | Covers | From |
|---|---|---|
| Project tools | The project row and its menu, the **Project** page and its log, runs under the tab's row, **Run the project's main action** (`Ctrl/Cmd+Shift+E`), **A project action finishes**, Settings → Project tools and the project's **Tools** page. | [Project tools](DESIGN.md#project-tools), [The project's pages](DESIGN.md#the-projects-pages) |
| Unreal Engine | Its actions, **Editor configuration**, finding the engine and **Choose engine folder…**, **Open solution** and **Open in Rider**, the ones that ask first. | [Project tools](DESIGN.md#project-tools) |
| Unity | Its actions, **Code optimization**, **Choose Unity editor…**, what's refused while Unity has the project open. | [Project tools](DESIGN.md#project-tools) |
| Godot | Its actions, the Godot executable, C# projects. | [Project tools](DESIGN.md#project-tools) |
| Actions and links | Your own: Settings → the project's **Actions** and **Links**, **Add an action…** and **Add a link…**, the two files, a link's placeholders, setting up each new worktree. Reference has the files' format. | [Project tools](DESIGN.md#project-tools), [The project's pages](DESIGN.md#the-projects-pages) |
| Perforce | **Keep Perforce logins fresh**, the password prompt, **Password source**, servers that need you to log in yourself, the changelist on the row, **Open in P4V**. | [Perforce ticket handling](DESIGN.md#perforce-ticket-handling), [Perforce changelist](DESIGN.md#perforce-changelist-in-the-tab-title) |

### Away from your desk (`away/`)

| Page | Covers | From |
|---|---|---|
| Notifications | Each kind and its switch, what they say, when they're skipped, where a click goes. | [§10](DESIGN.md#10-notifications) |
| The Dock and taskbar icon | The badge, the icon's animation, the taskbar's flash. | [§10](DESIGN.md#10-notifications) |
| The Claude app | **Connect to the Claude app**, **Open in the Claude app**, the phone icon, pushes to your phone, who can't use it. | [Remote Control](DESIGN.md#remote-control-the-claude-app) |
| Keeping the computer awake | Settings → General, the header's cup, when it holds off sleep. | [Keeping the computer awake](DESIGN.md#keeping-the-computer-awake) |
| Starting at login | **Start Claudette when I log in**, which copy starts, when the switch is turned off elsewhere. | [Starting at login](DESIGN.md#starting-at-login) |

### Claudette (`claudette/`)

| Page | Covers | From |
|---|---|---|
| Claudette on the composer | What she does and why, poking and carrying her, her hats, her settings, **Hide Claudette**. | [Claudette on the composer](DESIGN.md#claudette-on-the-composer) |
| Games with Claudette | Starting a game, the three games and their levels, the score. | [Games with Claudette](DESIGN.md#games-with-claudette) |

### Settings (`settings/`)

| Page | Covers | From |
|---|---|---|
| The Settings window | Opening it, its search, changes applying at once, **Reset to defaults** and what it leaves alone, the project's pages, the version and **Report an issue**. | [§14](DESIGN.md#14-settings), [Version](DESIGN.md#version) |
| Every setting | A table per category: each setting's label, its default, whether it syncs, whether a tab can override it, and the page that explains it. | [Categories](DESIGN.md#categories), [Settings sync](DESIGN.md#settings-sync-optional) |
| Styles and themes | **Theme**, **Style** (Standard and Claude, with pictures of both in light and dark), **Density**, the fonts, what the tab rows show. | [Visual style](DESIGN.md#visual-style) |
| Accessibility | Screen readers and what they hear, **Zoom**, **Motion**, and what the editor can't do yet. | [Accessibility](DESIGN.md#accessibility) |
| Keyboard | **Send with**, changing a shortcut, one key for both OSes, the shortcuts it refuses. Reference lists them all. | [Keyboard shortcuts](DESIGN.md#keyboard-shortcuts) |
| Advanced | **Extra arguments for every claude process**, **Log protocol traffic**, **Open log folder**, **Open data folder**, **Diagnostics**. | [§14](DESIGN.md#14-settings), [Staying tolerant at runtime](DESIGN.md#staying-tolerant-at-runtime) |

### Updates (`updates/`)

| Page | Covers | From |
|---|---|---|
| Claude Code's updates | The badge, what **Update now** does for each way of installing it, **Update on next launch**, **Dismiss**, tabs keeping their version until they restart, updates turned off. | [§12](DESIGN.md#12-claude-code-updates) |
| Claudette's updates | **Check for Claudette updates automatically** and **Include pre-releases**, the badge, **Restart to update**, **Update when idle**, **Skip this version**, copies that can't update themselves, upgrading from 0.1.0; what's new in each version is on the Changelog page. | [Updating Claudette](DESIGN.md#updating-claudette) |
| When an update fails | What comes back, installing by hand, moving Claudette to Applications. | [Updating Claudette](DESIGN.md#updating-claudette) |

### Troubleshooting (`troubleshooting/`)

| Page | Covers | From |
|---|---|---|
| Claude Code missing or too old | The setup screen, and the notes about a version newer or older than the one Claudette was tested with. | [§12](DESIGN.md#12-claude-code-updates), [Tested versions](DESIGN.md#tested-versions) |
| Signing in | When signing in fails, **Try again**, an organization that isn't allowed. | [§11](DESIGN.md#11-sign-in) |
| A tab that won't start | The reasons a tab gives, **Restart**, **Choose folder…**, a folder or a session that's gone. | [§4](DESIGN.md#4-tabs--sessions), [§13](DESIGN.md#13-architecture), [Restore on launch](DESIGN.md#restore-on-launch) |
| A tab that seems stuck | *Possibly stuck*, check-ins, **Stop**. | [Check-ins on long turns](DESIGN.md#check-ins-on-long-turns) |
| Claude can't find a program | On macOS and Linux, the login shell's environment, the shells it reads, **Use my login shell's environment**. | [Login shell environment](DESIGN.md#login-shell-environment) |
| Something missing on Linux | `notify-send` for notifications, `systemd-inhibit` for keeping awake, `secret-tool` for passwords and keys; and a `dotnet run` build's notifications on macOS. | [§10](DESIGN.md#10-notifications), [Keeping the computer awake](DESIGN.md#keeping-the-computer-awake), [Accounts](DESIGN.md#accounts) |
| Claude is having problems | The service status dot and banner, and **Show Claude's service status**. | [Service status](DESIGN.md#service-status) |
| Diagnostics and logs | **Diagnostics** and **Copy diagnostics**, `claudette.log`, the protocol log, the note when Claudette couldn't show part of a conversation. | [Staying tolerant at runtime](DESIGN.md#staying-tolerant-at-runtime), [§13](DESIGN.md#13-architecture) |
| Reporting an issue | Copying the versions, **Report an issue**. | [Version](DESIGN.md#version) |
| Settings back to their defaults | A settings file that isn't valid kept as `.bad`, and one that couldn't be read. | [Storage](DESIGN.md#storage) |

### Privacy and your data (`privacy/`)

| Page | Covers | From |
|---|---|---|
| Where Claudette keeps things | The data folder on each OS and `CLAUDETTE_HOME`, what's in it (settings, state, usage history, drafts, unsaved edits, logs, downloads), the session library's contents, and Claude Code's own files. | [Storage](DESIGN.md#storage), [§9](DESIGN.md#9-sessions-history-restore--sync), [Usage history](DESIGN.md#usage-history) |
| What leaves this computer | The session library and its synced folder, usage sharing, settings sync, the status page, the update check. What Claude Code sends is Claude Code's. | [Session library](DESIGN.md#session-library-sync-across-machines), [Sharing across machines](DESIGN.md#sharing-across-machines), [Service status](DESIGN.md#service-status), [Updating Claudette](DESIGN.md#updating-claudette) |
| Passwords and keys | Perforce passwords and API keys in the OS's credential store, never synced or logged. | [Perforce ticket handling](DESIGN.md#perforce-ticket-handling), [Accounts](DESIGN.md#accounts) |

### Reference (`reference/`)

| Page | Covers | From |
|---|---|---|
| Keyboard shortcuts | Every shortcut, with its default on Windows and Linux and on macOS; the fixed keys; the editor's keys. | [Keyboard shortcuts](DESIGN.md#keyboard-shortcuts), [Project files](DESIGN.md#project-files) |
| Icons and badges | Every status icon, badge and dot, what it means, and the page that explains it. | [Sidebar](DESIGN.md#sidebar), [§3](DESIGN.md#3-main-window) |
| Notifications | Every kind, its switch and what it says. | [§10](DESIGN.md#10-notifications) |
| Command line and environment | `--folder` and `--login`; `CLAUDETTE_HOME` and `CLAUDETTE_RUN_IN_PLACE`; Claude Code's own variables, which pass through to `claude`; what project actions get (`CLAUDETTE_WORKTREE`, `CLAUDETTE_MAIN_CHECKOUT`) and what a login shell sees (`CLAUDETTE_RESOLVING_ENVIRONMENT`). | [Opening a tab](DESIGN.md#opening-a-tab), [Integration with Claude Code](DESIGN.md#integration-with-claude-code), [Login shell environment](DESIGN.md#login-shell-environment), [Project tools](DESIGN.md#project-tools) |
| `claudette.json` | The format of `claudette.json` and `claudette.local.json`: actions and their fields, links and their placeholders, comments, and what's skipped. | [Project tools](DESIGN.md#project-tools) |
| Windows, macOS and Linux | What differs on each: installing, menus, notifications, the credential store, keeping awake, starting at login, what isn't tested yet. | [§2](DESIGN.md#2-platform--tech-stack), the pages' own notes |

### Changelog (`docs/changelog.md`)

One page, last in the sidebar: what changed in each version, newest first, with what's on `main` and not yet released at the top. Made from `CHANGELOG.md` ([Downloads and the changelog](#5-downloads-and-the-changelog)).

## 5. Downloads and the changelog

Two pages are made from data rather than written: the Downloads page from the repository's releases, and the Changelog page from `CHANGELOG.md`.

### The Downloads page

- **The latest release** comes first: its version and date, links to what's new in it (its section of the Changelog page) and to its release on GitHub, and a download for each package.
  - **Windows**: **x64 (Intel or AMD)** and **Arm64**, the MSIX files.
  - **macOS**: **Apple silicon** and **Intel**, the `.dmg` files.
  - **Linux**: no package, and a link to Getting started → **Running from source**.
  - Each package shows its file's name and size, and its SHA-256 digest, for anyone who wants to check the file (`Get-FileHash` in PowerShell, `shasum -a 256` on a Mac). Claudette's own updates check it themselves ([Updating Claudette](DESIGN.md#updating-claudette)).
- **Which one you need.** On Windows, Settings → System → About → **System type**. On a Mac, Apple menu → **About This Mac** → **Chip**: an Apple M-series chip is Apple silicon.
- **The next version**, when a pre-release is newer than the latest release: in a box of its own, marked as a pre-release, saying that Claudette offers pre-releases only with Settings → General → **Include pre-releases**.
- **Earlier releases**, newest first: each with its date, its packages, and links to its changelog section and its release. 0.1.0, from before the repository moved, says it can't update itself to a later version, which is installed by hand.
- Then links to Getting started → **What you need** and **Installing**, and to Updates → **Claudette's updates**: once installed, Claudette updates itself.
- **Which file is which.** `package.yml` names the packages `Claudette-<version>-<x64|arm64>.<msix|dmg>` ([Packaging and signing](DESIGN.md#packaging-and-signing)), as the update check expects. The page reads the OS from the extension and the processor from the name, and lists anything else attached to a release under **Other files**.
- **The data.** `docs/_scripts/prepare.sh` reads the repository's releases from the GitHub API and writes `docs/_data/releases.json`: the published releases (drafts never show), each with its tag, name, date, whether it's a pre-release, its page, and its packages' names, sizes, digests and download addresses. It also works out which is the latest release and whether a newer pre-release is the next version, by Semantic Versioning, so the page itself only lays them out.
- **Without JavaScript.** The page is built with the site, which is rebuilt whenever a release is published, edited, unpublished or deleted ([The site](#1-the-site)). Without the data, as in a preview that skipped `prepare.sh`, it links to the Releases page on GitHub instead.
- The home page's download button goes here, and so does the README's link, once the page is published.

### The changelog

- **`CHANGELOG.md`**, at the repository's root, says what changed in each version, for the people using Claudette. It follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/):
  - `## [Unreleased]` at the top, then `## [X.Y.Z] - YYYY-MM-DD` for each release, newest first.
  - Under each, only the headings it needs, in this order: `### Added`, `### Changed`, `### Deprecated`, `### Removed`, `### Fixed`, `### Security`.
  - At the bottom, a link for each version: Unreleased compared with the latest release, each release with the one before it, and the first release's own page.
- **What goes in.** Anything people using Claudette could notice: what they can do now, what works differently, what's going away or gone, what was fixed that they could have run into, and what affects their security. Not refactoring, tests, CI, or changes to DESIGN.md or this file.
  - A fix to something no release has had yet changes that thing's entry under **Unreleased**, rather than adding one under **Fixed**.
  - A change Claude Code made that Claudette now handles goes in when people can tell, such as the version Claudette is tested with.
- **How it's written.** As a guide page is ([Writing a page](#2-writing-a-page)): for the people using Claudette, in the app's own words in bold, saying what they can now do and where (Settings → Usage → **Keep usage history**), not how it was built. An entry is a sentence or two, starting with the thing that changed in bold when that helps the reader scan. It can link to the change's guide page by its full address once the page exists.
- **When.** In the same change as the code, as DESIGN.md is: a change people would notice adds its entry under **Unreleased**.
- **Releasing.**
  1. One change turns `## [Unreleased]` into the version with the day's date, starts an empty `## [Unreleased]` above it, and updates the links at the bottom. The version follows Semantic Versioning: until 1.0.0, a section with **Added**, **Changed**, **Deprecated** or **Removed** raises the second number, and **Fixed** or **Security** alone the third. It's the version `Directory.Build.props` names ([Version](DESIGN.md#version)).
  2. The tag `vX.Y.Z` goes on that change. `package.yml` drafts the release with the version's section as its notes, from `packaging/release-notes.sh` ([Packaging and signing](DESIGN.md#packaging-and-signing)), so the release on GitHub and Claudette's update dialog say what the Changelog page says. A tag whose version has no section fails before anything is built.
     - A pre-release (`v0.4.0-beta.1`) needs no section of its own: its notes are **Unreleased**'s, which stays as it is until the release.
  3. Publishing the release rebuilds the site: the Downloads page lists it, and the guide's *New in the next release* notes for it are removed in a change of their own.
  4. `Directory.Build.props` moves to the next version to work on.
- **The Changelog page** (`docs/changelog.md`) has its own title and introduction: what the version numbers mean, and that **Unreleased** is what's on `main` and in this guide, but not yet in a release. Under them it includes `docs/_includes/changelog.md`, which `prepare.sh` copies from `CHANGELOG.md` from its first version on, giving each version's heading an id (`v0.2.0`, and `unreleased`) for the Downloads page to link to. Without it, the page links to `CHANGELOG.md` on GitHub.

## 6. Coverage

The map covers every part of Claudette that DESIGN.md describes. When a chapter is written, its pages are checked against more than its DESIGN.md sections:

- The app's own lists: the Settings pages' search entries, the command palette's commands (`ShellViewModel.Palette.cs`), the shortcuts (`KeyboardShortcuts.cs`), and the menus (the tab's, the group's, the project's, the cards'). Each item in the chapter's part of the app is named on its page.
- The running app, for labels: where DESIGN.md's words differ from the app's, the page uses the app's, and DESIGN.md is corrected.

## 7. Keeping it current

- A change people can see updates its guide page and retakes its pictures in the same change, as it updates DESIGN.md, once its chapter is written. CLAUDE.md says so.
- A new part of Claudette in DESIGN.md gets its row in the map here in the same change, whether or not its chapter is written yet.
- A change people would notice adds its entry to `CHANGELOG.md`'s **Unreleased** section in the same change ([The changelog](#the-changelog)). CLAUDE.md says so too.
- The `issues` skill's plans include the page, and what it builds includes the changelog's entry.
- The README links to the guide at its top and under Getting started, once Getting started is published.

## 8. Steps

1. **DESIGN.md in line with the app**, since the pages are written from it.
   - Mark what's built. These have no ✅ in DESIGN.md but are in the app, so check each and mark it: tab marks and the reviewed icon, worktree tabs, a group's name and color, extra folders, **Ultracode**, **Output style**, **Zoom**, **Motion**, the side panel's page order, Claudette on the composer, Claude Docs, folder trust, the starting mode, keeping the computer awake, sharing usage, several prompts waiting, History's project chips. Project files says "not built yet" at its top and in §17, but its steps are all ✅ and it's merged.
   - Use the app's labels. Settings → Editor's (**The explorer is on the**, **Wrap long lines**, **Indent new lines with**, **Show a file clicked in a conversation**), **Reveal in file manager**, the banner for a file gone from disk (**Keep editing**), the indentation menu, General's **Name each tab's session in Claude Code after the tab** and Sessions' **Sync Claudette's settings through the library**.
   - The end of a turn: [Changes per turn](DESIGN.md#changes-per-turn) still describes the turn's footer, which [Conversation cards](DESIGN.md#conversation-cards) replaced with the **Modified files** and **Turn** cards.
   - The tab's menu in [Sidebar](DESIGN.md#sidebar) leaves out **Account ▸**, **Duplicate tab**, **Export conversation…** and **Close other tabs**, which other sections put in it.
2. ✅ **The changelog's releases**, done 2026-10-16. `package.yml` drafts a release with its version's section of `CHANGELOG.md` as the notes, in place of notes generated from pull requests, and `Directory.Build.props` moved to 0.3.0.
3. `guide-screenshots.ps1`, and what the demo needs for Getting started.
4. **The site, Downloads, the Changelog and Getting started.**
   - `_config.yml`, the theme in light and dark, the includes, and Settings → Pages → Source switched to **GitHub Actions**.
   - `docs.yml` and `prepare.sh`.
   - The home page, the Downloads page and the Changelog page.
   - The Getting started chapter in full, with its pictures.
   - The README's links, to the guide and to the Downloads page.

   Chapters join the sidebar once they're written, never as empty pages, since everything on `main` is published. The Downloads and Changelog pages can come before Getting started, since they need no pictures.
5. The other chapters, in the map's order, a pull request each, with their pictures and what the demo and the script need for them.

## 9. To decide later

- Whether Claudette links to the guide. It's what the user sees, so it's decided in DESIGN.md ([User guide](DESIGN.md#user-guide)).
- A check that the guide names everything: a test that reads `docs/` and fails when a Settings search entry, a shortcut or a palette command appears on no page. It would make [Coverage](#6-coverage) automatic, at the cost of tying the guide's words to the app's.
