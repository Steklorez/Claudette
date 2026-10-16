---
title: The prompt cache
parent: Usage and limits
nav_order: 6
---

# The prompt cache

{% include unreleased.html %}

Claude keeps a conversation in a cache for a while after each request that reads or writes it, for five minutes or an hour. A message you send in that time reads the conversation from the cache, at a fraction of the price, and on a plan for much less of your usage limits. After it, Claude reads the whole conversation again in full. Each tab shows how long its cache has left.

## The Cache chip

In the composer bar, after the context indicator, the chip reads *Cache 52m*. The minutes are whole and rounded down, so it never promises more than there is.

- In the last 3 minutes it turns amber: *Cache 2m*, then *Cache <1m*.
- Once the cache has run out it reads *Cache expired*, in muted text.

It doesn't show before a conversation's first request, since there's no cache yet. It doesn't show while a turn runs either, because the turn's requests keep the cache fresh and the working line shows instead. It does show while a prompt waits for you mid-turn, since nothing refreshes the cache then.

It's left out entirely when Claude Code isn't caching the tab's prompts, for example when `DISABLE_PROMPT_CACHING` is set.

## The tooltip

Hover over the chip for what the cache is, how long it lasts and why, and when it expires, in your local time: *Sent before 14:52, your next message reads this conversation from the cache. After that, Claude reads all of it again at the full price.* On a plan it says the whole read counts toward your usage limits instead.

The line about its length says where it came from: *1-hour cache, as Claude Code usually gives subscriptions*, *5-minute cache, as Claude Code uses with an API key*, *set by CLAUDE_CODE_PROMPT_CACHE_TTL*, or *seen in the last turn*. A plan only gets the hour within its included usage, which Claudette can't tell, so it says *usually*.

## When the clock starts

It starts at the end of each of the main conversation's requests. A subagent's requests have caches of their own, so they don't count. A tab you restore counts from Claude's last reply in its transcript, so reopening a tab within the hour shows what's left.

Clearing or compacting the conversation, switching the model (each model has its own cache) or [moving the tab to another account](../accounts/moving-a-tab-to-another-account.md) forgets the cache until the next request.

## How the length is chosen

Claude Code decides how long its cache lasts, and Claudette follows what Claude Code is set to. In order, it goes by Claude Code's own configuration (the variables `FORCE_PROMPT_CACHING_5M`, `CLAUDE_CODE_PROMPT_CACHE_TTL` and `ENABLE_PROMPT_CACHING_1H`, and the `promptCacheTtl` setting, as the tab's `claude` gets them), then by what Claude Code reported for the last request, then by its default: an hour on a plan, five minutes with an API key or a cloud provider. Claude Code's [docs](https://code.claude.com/docs) describe its settings.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Show when the prompt cache expires** | On | Settings → Appearance |

## See also

- [A tab's tokens](a-tabs-tokens.md) for the cache write and cache read counts.
- [Context and compacting](context-and-compacting.md), the chip's neighbor in the bar.
