---
title: Moving a tab to another account
parent: Accounts
nav_order: 4
summary: "You can move a tab that's open to another account and carry on with the same conversation."
---

# Moving a tab to another account

You can move a tab that's open to another account and carry on with the same conversation. It costs one re-read of the conversation, which Claudette tells you about first.

## Moving it

1. Right-click the tab in the sidebar.
2. Choose **Account ▸**, then the account to move to. The tab's current one is ticked. The menu shows once you have an account besides Default.
3. Read the question, such as *Move "Grappling hook" to Studio?*, and click **Move**.

Claudette starts the tab's Claude Code again under the account, resuming the session, and adds a note to the conversation: *Started Claude Code again under Studio.* A tab with no conversation yet moves without asking.

If Claude is working, the move waits for the turn to end, as a change to a tab's extra folders does. The conversation says *Moves to Studio once this turn ends, when Claude Code starts again.*

Moving copies nothing: tabs on any account share Claude Code's settings, transcripts and History, so the conversation is already there.

## What it costs

The prompt cache is never shared between organizations, and thinking Claude did under one is dropped under another. So the next turn reads the whole conversation again, once, without the cache. The question says what that comes to:

- Moving to an API key: *Claude reads the conversation's 150,000 tokens again under Studio, without the prompt cache, once: about $0.75 at list price.* The amount is the conversation's size times the model's input price, times 1.25, the price of writing it to a five-minute cache. A model Claudette doesn't have a price for gets no amount, and the question says it's billed to the key.
- Moving to your plan: the question says the read counts toward its limits instead.

The tab's [Cache chip](../usage/the-prompt-cache.md) forgets its timer until the next request.

## Moving back

Do the same and choose **Default**, or the other account. Each move reads the conversation once.

## See also

- [A folder's account](a-folders-account.md) for choosing the account new tabs use.
- [The prompt cache](../usage/the-prompt-cache.md) for why a cache doesn't carry over.
- [The tab's menu](../tabs/the-tabs-menu.md).
