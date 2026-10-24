---
title: The session library
parent: History and syncing
nav_order: 2
summary: "The session library is Claudette's own folder of sessions."
---

# The session library

The session library is Claudette's own folder of sessions. A tab that [syncs](syncing-a-tab.md) copies its session there after every turn, and History lists what's in it next to the sessions Claude Code kept on this computer. If the folder is one a sync client keeps up to date, your other machines see the same sessions and can [pick them up](picking-up-on-another-machine.md).

Claudette doesn't talk to Google Drive or any other cloud service. It reads and writes files in the folder, and your sync client does the rest.

## Where the library is

By default it's the `library` folder inside [Claudette's data folder](../privacy/where-claudette-keeps-things.md), where no other machine sees it. To share it, point it at a folder your sync client keeps in sync.

1. Open Settings → Sessions.
2. Under **Session library**, beside **Folder**, choose **Browse…** to use another folder from now on, or **Move library…** to copy the sessions already in the library to the new folder and use that.
3. Pick the folder.

Under the folder, the page says how much the library holds: *The library holds 1.4 GB: 23 sessions.* That's everything in it, scratch pads and shared usage included, and it's measured each time you open the page. [Syncing a tab](syncing-a-tab.md#how-much-it-takes) shows what each session takes.

**Browse…** leaves the existing sessions where they were. **Move library…** copies them and says *Copied the sessions to …. The old folder was left as it was.* Neither deletes anything. **Use the default folder** goes back to the folder inside the data folder.

Use the same folder, as your sync client sees it, on every machine. The folder's path can differ from one machine to the next.

### A cloud-synced folder

Transcripts hold your code, command output and anything else Claude read in your projects. When you pick a folder that Google Drive, Dropbox, OneDrive, iCloud Drive or Box is keeping in sync, Claudette says so and asks before using it: *This folder is synced by Dropbox. Session transcripts contain your code, command output and anything else Claude read in your projects, and they'll be uploaded there. Use it anyway?* Choose **Use this folder** to go on, or **Cancel**.

{: .warning }
Anyone who can read the synced folder can read your sessions. Keep it in an account and a folder only you can reach.

## What's in the library

Each synced session has a folder of its own with:

- A record: the tab's name and mark, its model, effort and per-tab choices, its token counts, which machine used it last and when, when it was last opened, what project it belongs to (the git remote, the branch and the folder inside the repository), and which changed files you ticked as reviewed.
- A copy of Claude Code's transcript, with the subagents' transcripts.
- A usage file, while you [share usage](../usage/sharing-usage-across-machines.md): each machine's turns in the session, counts only.

The library also holds the file that [settings sync](settings-sync.md) uses, a `usage` folder with a file for each machine that shares its plan usage, and a `scratch` folder with the [scratch pads](../conversation/the-scratch-pad.md) of projects that have a git remote.

Claude Code's sign-in and settings are never copied.

Claudette writes each file under a temporary name and renames it when it's whole, so a sync client never uploads half a file.

## Naming this machine

Settings → Sessions → **Name for this machine** is the name History shows for a session another machine used last, and the name in notes like *in use on DESKTOP-01*. Leave it empty to use the computer's name. Give your machines names you'll tell apart.

## Keeping sessions

Claude Code deletes its own transcripts after 30 days by default. The library's copies aren't affected, so for the sessions that sync, the library is also a longer-term archive.

Settings → Sessions → **Delete sessions not used for a while** says how long they stay. It's on to start with, and **After going unused for (days)**, under it, is 15. Once no machine has used or opened a session for that many days, Claudette deletes it from the library. Claudette checks when it starts, then once a day while it's open. Turn the switch off to keep sessions in the library for good.

- A session is used when a tab that syncs copies it to the library: after each turn, when you turn syncing on, with **Sync now**, and after you tick files as reviewed.
- Opening a session from History counts too, even if you don't send anything. History still says which machine last used it.
- Claudette never deletes a session that's open in a tab that syncs, or one that another machine is using. A library copy of a tab that no longer syncs is deleted like any other.

{: .warning }
The library is shared, but this setting is each machine's own. A machine set to fewer days deletes sessions from the library for all your machines, and they leave History on every one of them. Give each machine the same number of days, or the fewest you'd want anywhere.

Up to version 0.5.1 this was **Keep library sessions for**, with the choices **1 month**, **1 year** and **Forever**. If you'd chosen 1 month or 1 year, it's now 31 or 366 days. Forever, the old default, became 15 days.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Folder** | Inside the data folder | Settings → Sessions → Session library |
| **Name for this machine** | The computer's name | Settings → Sessions |
| **Delete sessions not used for a while** | On, after 15 days | Settings → Sessions |
| **Sync new tabs to the session library** | Off | Settings → Sessions, explained in [Syncing a tab](syncing-a-tab.md) |

These settings, like the library's folder, stay on each machine. **Reset to defaults** leaves the library folder and settings sync as they are, since changing either moves where your sessions and settings live.

## See also

- [Syncing a tab](syncing-a-tab.md) for choosing which sessions go into the library.
- [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md) and [What leaves this computer](../privacy/what-leaves-this-computer.md) for the whole picture of your data.
