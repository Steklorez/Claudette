---
title: Earlier prompts, drafts and the stash
parent: The composer
nav_order: 5
---

# Earlier prompts, drafts and the stash

What you type isn't lost. Each tab brings back its earlier prompts, keeps your unsent message when you quit, and lets you put a message aside to use later, in this tab or any other.

## Bring back an earlier prompt

Press `Up` on the message box's first line to bring back the tab's previous prompt, as a terminal's history does. Press it again for the one before. `Down` on the last line comes forward again, back to what you were typing.

- Typing ends the recall, so the next `Up` starts from the newest again.
- The same prompt twice in a row is kept once, and a tab keeps its last 200.
- A restored tab has the prompts of its transcript too.
- With the `/` or `@` list open, the arrows move in the list instead ([Slash commands and mentions](slash-commands-and-mentions.md)).

## Drafts

Each tab's unsent message is written to disk a second after you last changed it, and again when the tab closes. It holds your text, any [quick suffixes](quick-suffixes.md) you added for this one message, and attached images and large pastes ([Attachments and large pastes](attachments-and-large-pastes.md)). When Claudette starts again, a restored tab has its draft back in the box.

- Sending empties the draft. Closing a tab, rather than quitting, deletes it.
- A tab that isn't restored at launch loses its draft. That's the case for unpinned tabs when Settings → Sessions → **Also restore unpinned tabs when Claudette starts** is off ([Closing and restoring](../tabs/closing-and-restoring.md)).

## The stash

The stash holds whole messages you've put aside. It's for a message that belongs somewhere else, or later. Every tab shares it.

### Put a message aside

Press `Ctrl/Cmd+S`, or choose **Stash the message** in the [command palette](../tabs/the-command-palette.md), which lists it while there's something typed. The whole draft, with its suffixes, images and pastes, leaves the box and goes to the stash.

### Take one back

While the stash holds anything, the bar has **Stashed (2) ▾** after **Suffixes ▾**. Its menu lists the messages, newest first. Each shows its first line (or what's attached, such as *2 images*), the folder it was written in and how long ago, such as *api · 5 min ago*. Hover one to see all of its text.

1. Open **Stashed ▾**.
2. Pick a message. It leaves the stash and goes into this tab's box, ahead of anything you've typed.

The `×` beside a message deletes it from the stash.

The stash is saved on disk, so it outlives quitting. It belongs to this computer and isn't synced to your other machines.

## Settings

Nothing here has a switch. The shortcut can be changed in Settings → Keyboard → **Stash the message**, which defaults to `Ctrl/Cmd+S`.

## Shortcuts

| | Windows and Linux | macOS |
|---|---|---|
| Earlier prompt, later prompt | `Up`, `Down` | `Up`, `Down` |
| Stash the message | `Ctrl+S` | `Cmd+S` |

## See also

- [Sending and stopping](sending-and-stopping.md): a message taken back from the queue returns to the box the same way.
