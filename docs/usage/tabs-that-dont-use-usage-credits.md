---
title: Tabs that don't use usage credits
parent: Usage and limits
nav_order: 9
summary: "Once a limit is used up, a tab can stop and wait for the reset rather than spend your usage credits."
---

# Tabs that don't use usage credits

If your plan has usage credits turned on, they pay for Claude once your session or weekly limit is used up, for every tab alike. You can keep them for the work that matters: a tab that doesn't use them stops instead, and waits for the limit to reset, as it would at a limit with no credits behind it.

## Choosing

- **For every tab:** Settings → Tabs → **Use usage credits once a limit is used up**. It's on by default.
- **For one tab:** **Use usage credits** in the tab's menu, a check item. Untick it on the tabs that can wait, or turn the setting off and tick it on the ones that can't. **Tab settings…** has the same choice, also named **Use usage credits**: see [Tab settings](../tabs/tab-settings.md).

The menu keeps the tab's own choice only while it differs from Settings, so ticking it back to what Settings says leaves the tab following Settings again, and the dot beside **Tab settings…** goes.

Only tabs on your plan's account have the choice. A tab on an [API-key account](../accounts/api-key-accounts.md) pays for every request with its key, so it has no usage credits to save.

## What the tab does

Claude Code itself can't turn usage credits off for one session, so Claudette does it, in two ways.

- **Before sending.** While usage credits pay (the [notice under the header](the-usage-header.md#when-usage-credits-pay) says so), a message you send isn't sent. It waits in the conversation, marked *Queued: sent when the usage limit resets*, with **Send now**, which sends it anyway, paid for with credits, and **Cancel**, which takes it back to the composer. So do the task list's **Continue** and a thread's messages to the tab.
- **During a turn.** When a turn runs into the limit and credits begin paying for it, Claudette stops the turn. The request that ran into the limit is already paid for, since Claude Code says so only after it. The conversation gets a note: *Usage credits began paying for Claude, and this tab doesn't use them, so Claudette stopped the turn.* Messages that were waiting their turn are held for the reset too. A turn you sent with **Send now** isn't stopped.

## Waiting for the reset

Either way, the tab waits for the limit to reset exactly as it does after a usage limit: the bar over the composer, the tab's row, the hourglass on the Dock or taskbar icon, and a restart keeps waiting. See [When a limit stops a task](when-a-limit-stops-a-task.md). The bar says why:

> *Your session limit is used up, and this tab doesn't use usage credits, so Claude stopped. The task continues by itself when it resets, at 15:45.*

- **A stopped task** continues at the reset by itself, unless auto-continue is off for the tab, and **Don't continue** works as it does at a limit.
- **A held message** goes at the reset in place of *Continue from where you left off*, as a message you sent during a wait would, even with auto-continue off, since you sent it. The bar says *Your message is sent when it resets*. If nothing was stopped, the bar has no **Don't continue** or close button: **Cancel** on the message takes it back, and the bar goes with it.
- **Use usage credits** on the bar turns them on for the tab and carries on now. Ticking **Use usage credits** in the menu, or turning the setting on, does the same.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Use usage credits once a limit is used up** | On | Settings → Tabs → At a usage limit |
| **Use usage credits** | Follows the setting | The tab's menu, or **Tab settings…** |

## See also

- [The usage header](the-usage-header.md#when-usage-credits-pay), for the notice while credits pay and **Manage on claude.ai**, where you turn them off for every tab or cap them.
- [When a limit stops a task](when-a-limit-stops-a-task.md), the wait these tabs share.
