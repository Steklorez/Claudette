---
title: The scratch pad
parent: The conversation
nav_order: 8
summary: "The scratch pad is a place for notes beside the conversation: what to check later, a command worth keeping, a piece of a reply."
---

# The scratch pad

The scratch pad is a place for notes beside the conversation: what to check later, a command worth keeping, a piece of a reply. Each project has one pad, shared by every tab in that project, and by the same project on your other machines.

{% include shot.html name="guide/conversation/scratch-pad" alt="The Scratch pad tool window, with notes and a command kept from a conversation." %}

Nothing in it goes to Claude unless you paste it into a message.

## Opening it

The **Scratch pad** is a tool window, the last at the right until you move it, and it is always there ([Tool windows](../tabs/tool-windows.md)). The command palette's **Show scratch pad** opens it too.

Its first line says whose pad it is, named as the tab group is: *Shared by the tabs in api*. **Copy all** copies the whole pad. **Clear…** empties it, after asking, for every tab of the project and on your other machines.

Under that, a line says where the pad is kept:

- *Synced to your other machines through the session library.*
- *On this machine. To see it on your other machines too, keep the session library in a synced folder (Settings → Sessions).*
- *On this machine only: the repository has no remote to know it by on another machine.*
- *On this machine only: the folder isn't in a git repository.*

The rest is the text: plain text in the code font, so pasted code keeps its columns. What you type saves a second after you stop.

## Adding to it

1. Select text in the conversation and right-click it, and choose **Add to scratch pad**. This works in a prompt, a reply, thinking, a tool's input or output, a note, and anything in a subagent's group.
2. Or, with nothing selected, right-click a message and choose **Add message to scratch pad**.
3. Or use the **Add to scratch pad** button on a message's card, or on a code block's header.

The text goes at the end after a blank line, its tool window shows, and the added text is selected, without taking the keyboard from where it was. A screen reader hears *Added to the scratch pad.* [Copying and quoting](copying-and-quoting.md) has the menus.

## Which pad a project has

A project is known by its git remote and the folder's path inside the repository. So every branch, worktree and clone of it shares one pad, and a tab group for a subfolder has its own. Without a remote, the pad stays on this machine.

## Syncing to other machines

A pad in a project with a remote is synced through the [session library](../history/the-session-library.md), whichever folder the library is in. It syncs after each save, when the page shows, when Claudette comes to the front, and once a minute. If the library's folder isn't synced to your other machines, the pad stays on this one.

If two machines changed the pad without seeing each other's changes, nothing is lost. A banner over the pad says *DESKTOP-01 also changed this scratch pad (14:05). Which should it keep?*, shows their text and offers:

- **Keep both**: the lines the two share once, with this machine's additions, then theirs.
- **Use theirs**
- **Keep mine**

Your choice carries over to the other machine without asking again. A sync tool's conflict copy of the file is asked about the same way.

If the library can't be reached, the pad stays saved here, the line under its title turns to the caution color and says why, and Claudette tries again at the next sync. A pad written by a newer Claudette is left alone, and the line says to update.

## What it isn't part of

[Find](finding-your-way.md#find), [Exporting](exporting.md) and the conversation don't include it. [Settings sync](../history/settings-sync.md) doesn't carry it, and the library's cleanup of old sessions leaves it alone.

## See also

- [Copying and quoting](copying-and-quoting.md)
- [The session library](../history/the-session-library.md)
