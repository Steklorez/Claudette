---
title: Moving a tab to another account
parent: Accounts
nav_order: 4
summary: "You can move a tab that's open to another account and carry on with the same conversation."
---

# Moving a tab to another account

You can move a tab that's open to another account and carry on with the same conversation. It costs one re-read of the conversation, which Deventry tells you about first.

## Moving it

1. Right-click the tab in the sidebar.
2. Choose **Account ▸**, then the account to move to. The tab's current one is ticked. The menu shows once you have an account besides Default.
3. Read the question, such as *Move "Grappling hook" to Studio?*, and click **Move**.

Deventry starts the tab's Claude Code again under the account, resuming the session, and adds a note to the conversation: *Started Claude Code again under Studio.* A tab with no conversation yet moves without asking.

If Claude is working, the move waits for the turn to end, as a change to a tab's extra folders does. The conversation says *Moves to Studio once this turn ends, when Claude Code starts again.*

Moving copies nothing: tabs on any account share Claude Code's settings, transcripts and History, so the conversation is already there.

## What it costs

The prompt cache is never shared between organizations, and thinking Claude did under one is dropped under another. So the next turn reads the whole conversation again, once, without the cache. The question says what that comes to:

- Moving to an API key: *Claude reads the conversation's 150,000 tokens again under Studio, without the prompt cache, once: about $0.75 at list price.* The amount is the conversation's size times the model's input price, times 1.25, the price of writing it to a five-minute cache. A model Deventry doesn't have a price for gets no amount, and the question says it's billed to the key.
- Moving to your plan: the question says the read counts toward its limits instead.

The tab's [Cache chip](../usage/the-prompt-cache.md) forgets its timer until the next request.

## Moving back

Do the same and choose **Default**, or the other account. Each move reads the conversation once.

## A conversation started under another account

{% include unreleased.html %}

Deventry remembers which account each conversation began under. If one would carry on under another, say because its folder now uses another account, you're signed in to Claude with another email, or you opened it from History in a tab on another account, Deventry sends nothing at first. The composer asks **Continue under another account?**, saying which account the conversation began under and which it would continue under: the whole earlier conversation goes to that account, and its organization may be able to see it.

- **Continue here** carries on under the new account. The tab doesn't ask again about that conversation and account.
- **Use Studio instead** moves the tab back to the account the conversation began under. It's there when that's one of your accounts in Deventry, such as Default or an API key, rather than another Claude sign-in.
- **New chat** starts a new conversation in the tab, with nothing of the earlier one. The earlier one stays in [History](../history/history.md).
- **New chat with a digest** does the same, and puts a short digest of the earlier conversation in the composer: your prompts and Claude's answers, without tool calls or thinking. Trim it as you like; nothing of it goes until you send it.
- **Cancel** leaves the tab as it was, without starting Claude Code. A message you sent while it asked is back in the composer.

Moving a tab with **Account ▸** has asked already, so it doesn't ask again. Deventry only knows the conversations it saw begin on this computer: one begun in a terminal, on another computer, or before this version doesn't ask.

## Settings

Settings → Accounts → **Warn before continuing a session under another account**, on by default, asks before a conversation carries on under another account.

## See also

- [A folder's account](a-folders-account.md) for choosing the account new tabs use.
- [The prompt cache](../usage/the-prompt-cache.md) for why a cache doesn't carry over.
- [The tab's menu](../tabs/the-tabs-menu.md).
