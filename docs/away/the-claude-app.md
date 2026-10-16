---
title: The Claude app
parent: Away from your desk
nav_order: 3
summary: "A tab can be used from the Claude app on your phone, or at claude.ai/code in a browser, while Claudette keeps running it on your computer."
---

# The Claude app

A tab can be used from the Claude app on your phone, or at claude.ai/code in a browser, while Claudette keeps running it on your computer. You can start a task at your desk, then watch it, send messages and answer its questions from the couch. Claude Code's [Remote Control](https://code.claude.com/docs/en/remote-control) does the work: your tab's Claude Code connects out to claude.ai, the app shows the conversation, and every tool still runs on this computer. Nothing connects in to your computer.

Each tab is its own session in the app, and each tab chooses for itself. It's off unless you turn it on.

## Connecting a tab

1. Open the tab's menu, or **Tab settings…**.
2. Check **Connect to the Claude app**.

The tab connects straight away, or when the running turn ends. It says *Connected to the Claude app.* in the conversation, with a link to the session. While it's connected, its row has a phone icon, dimmed while it connects or reconnects. The tab's [info card](../tabs/the-tab-info-card.md) has a **Claude app** row that says where it stands.

The switch is saved with the tab, so a connected tab connects again whenever its session starts: after you restore it, resume it or restart it. It's the tab's own choice, so **Use defaults** in Tab settings leaves it alone.

To connect every new tab, turn on Settings → Claude Code → **Connect new tabs to the Claude app**. It only sets the switch for tabs you open from then on; open tabs keep theirs.

## Opening the session

Choose **Open in the Claude app** in a connected tab's menu. It opens the session at claude.ai/code in your browser. On a phone, the same link opens the app.

## Answering from your phone

When you answer a permission prompt, question or plan in the app, the card in Claudette closes and reads *Answered in the Claude app*. It isn't an error, and the tab's *Needs input* status and its notification go.

## Pushes to your phone

The app can send a push when a tab needs an answer. Claude Code sends it, so you turn it on there: in Claude Code's `/config`, turn on **Push when actions required**, and **Push when Claude decides** if you like. Claudette doesn't change Claude Code's settings for this. Claude Code's page on [mobile push notifications](https://code.claude.com/docs/en/remote-control#mobile-push-notifications) has the steps.

Pushes don't arrive while Claudette's window is in front. Claudette tells Claude Code when you're at it, so your phone buzzes only when you're away. Claudette's own [notifications](notifications.md) on the computer are unchanged.

## Disconnecting

Uncheck **Connect to the Claude app**. If Claude is working, the tab says *Disconnecting from the Claude app when Claude finishes this turn.* and its phone icon fades until the connection has closed. Turn it back on before the turn ends and it stays connected, saying *Staying connected to the Claude app.*

## Who can't use it

Remote Control needs a claude.ai subscription: Pro, Max, Team or Enterprise (on Team and Enterprise an owner turns it on), signed in through claude.ai. It doesn't work with:

- An API key or an API key helper.
- A cloud provider such as Bedrock, Google Cloud or Foundry.
- A custom `ANTHROPIC_BASE_URL`, or a token from `claude setup-token` or in `ANTHROPIC_AUTH_TOKEN`.
- `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`.

When Claudette can tell your account can't use it, Settings → Claude Code says *Not available:* with the reason, and every tab's switch is disabled. A tab whose switch was already on says why it didn't connect, and can still be turned off. Claude Code checks the rest when a tab connects, such as your plan, your organization's policy and feature flags, and the tab shows its reason. If a policy turns Remote Control off, no tab tries again until Claudette restarts or another account signs in.

{% include unreleased.html %}

A tab that runs under one of Claudette's [API-key accounts](../accounts/api-key-accounts.md) can't connect either, whatever your Default account can do.

## Privacy

While a tab is connected, its transcript, with your messages, Claude's replies and its tool activity, is stored on Anthropic's servers under Anthropic's data usage policy. The files and the commands stay on your computer. That's why the switch is per tab and off by default. See [What leaves this computer](../privacy/what-leaves-this-computer.md).

## Keeping the computer awake

A sleeping computer can't be reached from your phone. While at least one tab is connected, Claudette keeps the computer from sleeping, as long as Settings → Claude Code → **Keep this computer awake while tabs are connected** is on (the default). The screen can still turn off. See [Keeping the computer awake](keeping-the-computer-awake.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Connect to the Claude app** | Off, for each tab | The tab's menu, or **Tab settings…** |
| **Connect new tabs to the Claude app** | Off | Settings → Claude Code → Claude app (Remote Control) |
| **Keep this computer awake while tabs are connected** | On | Settings → Claude Code → Claude app (Remote Control) |

These stay on this machine.

## See also

- [Notifications](notifications.md), the ones on this computer.
- [Permission cards](../permissions/permission-cards.md), the cards the app can answer.
