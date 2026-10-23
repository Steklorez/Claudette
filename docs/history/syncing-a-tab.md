---
title: Syncing a tab
parent: History and syncing
nav_order: 3
summary: "Syncing is a choice each tab makes."
---

# Syncing a tab

Syncing is a choice each tab makes. A tab that syncs copies its session to the [session library](the-session-library.md) after every turn, so you can continue it on another machine. A tab that doesn't sync never writes to the library: no copy, no record and no claim on the session. That way you only bring over the work you know you'll pick up elsewhere.

A tab that syncs has a small icon of two arrows chasing round a circle on its row, with the tip *Synced to the session library*, and once its session is there, how much it takes: *Synced to the session library · 312 MB*.

## Turning it on or off for a tab

1. Open the tab's menu, or **Tab settings…**.
2. Check or uncheck **Sync to other machines**.

- **Turning it on** copies the session to the library straight away, or when the turn ends if Claude is working. If another machine has the session open, sync stays off and the tab says so, because copying from here would overwrite what the other machine wrote.
- **Turning it off** stops the copying and lets go of this tab's claim on the session. The copy already in the library stays as it was, so other machines can still open it.

The choice is saved with the tab. It's the tab's own, not one of the per-tab overrides, so **Use defaults** in Tab settings leaves it alone.

## New tabs

Settings → Sessions → **Sync new tabs to the session library** (off by default) decides for tabs you open from now on, however you open them. Open tabs keep their own choice.

A session you open from [History](history.md) syncs when it came from the library or already has a record there, because someone chose to sync it. A session that only ever lived on this machine opens with sync off.

## When it copies

After each turn finishes, a moment after Claude's last message so Claude Code is done writing. After the first copy, only the new part of the conversation is added. Ticking a changed file as [reviewed](../changes/reviewed-files.md) copies the tab's record a couple of seconds later, so ticking several files writes it once.

If a copy fails, because the library's drive isn't there, say, the conversation says so once: *Couldn't copy this session to the session library: … It's tried again after the next turn, or use Sync now.* When a later copy works, it says *Copied this session to the session library again.*

## Sync now

**Sync now**, in the menu of a tab that syncs, copies the session right away instead of after the next turn. It's for catching the library up when you haven't sent a message: after a failed copy, after renaming the tab, or just before you leave the machine. It copies every file again, and the conversation says how it went: *Copied this session to the session library.*, or a warning with the reason.

It's disabled, with the reason in its tip, in these cases:

- Claude is working. The copy follows when the turn ends.
- The session has no message yet, or it's a copy that hasn't had its first message.
- The tab is read-only because another machine took the session over.

## How much it takes

A long session can grow to hundreds of megabytes, with the images and command output in it, and your sync client uploads and keeps all of it. A tab that syncs says how much its session takes in the library:

- The tip of its sync icon: *Synced to the session library · 312 MB*.
- The **Sync size** row of its [info card](../tabs/the-tab-info-card.md): *312 MB in the session library*.
- **Tab settings…**, under **Sync to other machines**: *This session takes 312 MB in the session library.*

It counts the session's conversation, its subagents' conversations and its record, and any copies your sync client made when two machines changed the session at once. It's measured each time the tab copies the session there, after a turn or from **Sync now**, and when a session that's already in the library starts. Until the first copy, it doesn't show.

[History](history.md) shows how much each session in the library takes, and Settings → Sessions how much the whole library holds; see [The session library](the-session-library.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Sync to other machines** | Off, for each tab | The tab's menu, or **Tab settings…** |
| **Sync new tabs to the session library** | Off | Settings → Sessions |

## See also

- [Picking up on another machine](picking-up-on-another-machine.md) for what happens when two machines want the same session.
- [Tab settings](../tabs/tab-settings.md) for the other choices a tab can make for itself.
