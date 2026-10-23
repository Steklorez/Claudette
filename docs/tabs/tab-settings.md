---
title: Tab settings
parent: Tabs and sessions
nav_order: 7
summary: "Some settings can be different for one tab."
---

# Tab settings

Some settings can be different for one tab. They replace the defaults from Settings for that tab only, and they're saved with it. A tab that has settings of its own shows a small dot beside **Tab settings…** in its menu.

## Opening the dialog

Right-click the tab and choose **Tab settings…**, or use **Tab settings…** in the [command palette](the-command-palette.md) for the selected tab. The dialog is titled *Settings for "‹the tab's name›"*.

## The overrides

Each one starts at **Default**, which says what the default is, such as *Default (Opus)*. Pick something else to give the tab its own.

| Setting | Choices | The default comes from |
|---|---|---|
| **Model** | Default, or a model | Settings → New tabs → **Default model** |
| **Effort** | Default, or an effort level | Settings → New tabs → **Default effort** |
| **Permission mode** | Default, or a mode | Settings → New tabs → **Default permission mode** |
| **Process monitor** | Default, **On**, **Off** | Settings → Processes → **Show the process monitor** |
| **Auto-continue** | Default, **On**, **Off** | Settings → Usage → **Continue tasks when a usage limit resets** |
| **Usage credits** | Default, **On**, **Off** | Settings → Usage → **Use usage credits once a limit is used up** |
| **Subagents** | Default, **Claude decides**, **Discourage them**, **Don't allow them** | Settings → Claude Code → **Subagents** |

- **Model** and **Effort** are explained in [Model, effort and output style](../composer/model-effort-and-output-style.md), and **Permission mode** in [Permission modes](../permissions/permission-modes.md). A change reaches the running session as soon as you apply it.
- **Process monitor** turns the [Processes](processes.md) page and the composer bar's summary on or off for this tab.
- **Auto-continue** is whether Claude carries on a task by itself when a usage limit resets. See [When a limit stops a task](../usage/when-a-limit-stops-a-task.md).
- **Usage credits** is whether the tab carries on with your plan's usage credits once a limit is used up, or stops and waits for the reset. The tab's menu has it too, as **Use usage credits**, and a tab on an API-key account doesn't have it. See [Tabs that don't use usage credits](../usage/tabs-that-dont-use-usage-credits.md).
- **Subagents** is whether Claude hands parts of its work to subagents in this tab. It applies as soon as you apply it, and Claude hears about it with your next message. See [Discouraging or turning off subagents](../conversation/subagents.md#discouraging-or-turning-off-subagents).

### Check-ins

Tick **Use different check-in settings for this tab** to set its own: **Check-ins** (**On**), **Run time (min)**, **Quiet time (min)**, the **Message**, and **Notify me when a check-in is sent**. Leave it unticked to use Settings → Check-ins. See [Check-ins on long turns](../composer/check-ins-on-long-turns.md).

### Applying and clearing

- **Apply** saves the dialog and closes it.
- **Cancel** closes it without changing anything.
- **Use defaults** sets the five overrides above back to **Default** and unticks the check-in box, in the dialog. Choose **Apply** to keep that. It leaves the three settings below alone.

## Settings that aren't overrides

These are in the same dialog, but they're the tab's own choices rather than replacements for a default. **Use defaults** leaves them as they are, and they don't count toward the dot.

- **Sync to other machines** copies the tab's session to the session library after each turn. It's the same switch as the tab's menu. Once the session is there, the line under it says how much it takes: *This session takes 312 MB in the session library.* See [Syncing a tab](../history/syncing-a-tab.md).
- **Connect to the Claude app** connects the tab to the Claude app on your phone or at claude.ai/code while it runs here, reconnecting whenever the tab starts. It's greyed out, with the reason under it, when the account can't use it. See [The Claude app](../away/the-claude-app.md).
- **Extra folders** are folders Claude may also read and edit, besides the tab's own. Choose **Add folder…**, or the remove button beside a folder. Their own settings and `CLAUDE.md` aren't loaded, and a folder that's gone isn't passed on.

Claude Code takes extra folders when it starts, so changing them starts it again, carrying the conversation on. When Claude is working, that waits until it has nothing left to do: the turn under way, and any messages you sent meanwhile, run first. Extra folders are saved with the tab, copied by **Duplicate tab**, and listed on the [info card](the-tab-info-card.md).

## Project actions

A folder's own actions belong to the folder, not the tab. The dialog says where they are (*Project actions are in Settings → ‹project› → Actions*) and has an **Open** button that goes there. See [Actions and links](../project-tools/actions-and-links.md).

## See also

- [The tab's menu](the-tabs-menu.md), which has the same **Sync to other machines** and **Connect to the Claude app** switches.
- [The Settings window](../settings/the-settings-window.md), for the defaults.
