---
title: The session library
parent: History and syncing
nav_order: 2
---

# The session library

The session library is Claudette's own folder of sessions. A tab that [syncs](syncing-a-tab.md) copies its session there after every turn, and History lists what's in it next to the sessions Claude Code kept on this computer. If the folder is one a sync client keeps up to date, your other machines see the same sessions and can [pick them up](picking-up-on-another-machine.md).

Claudette doesn't talk to Google Drive or any other cloud service. It reads and writes files in the folder, and your sync client does the rest.

## Where the library is

By default it's the `library` folder inside [Claudette's data folder](../privacy/where-claudette-keeps-things.md), where no other machine sees it. To share it, point it at a folder your sync client keeps in sync.

1. Open Settings → Sessions.
2. Under **Session library folder**, choose **Browse…** to use another folder from now on, or **Move library…** to copy the sessions already in the library to the new folder and use that.
3. Pick the folder.

**Browse…** leaves the existing sessions where they were. **Move library…** copies them and says *Copied the sessions to …. The old folder was left as it was.* Neither deletes anything. **Use the default folder** goes back to the folder inside the data folder.

Use the same folder, as your sync client sees it, on every machine. The folder's path can differ from one machine to the next.

### A cloud-synced folder

Transcripts hold your code, command output and anything else Claude read in your projects. When you pick a folder that Google Drive, Dropbox, OneDrive, iCloud Drive or Box is keeping in sync, Claudette says so and asks before using it: *This folder is synced by Dropbox. Session transcripts contain your code, command output and anything else Claude read in your projects, and they'll be uploaded there. Use it anyway?* Choose **Use this folder** to go on, or **Cancel**.

{: .warning }
Anyone who can read the synced folder can read your sessions. Keep it in an account and a folder only you can reach.

## What's in the library

Each synced session has a folder of its own with:

- A record: the tab's name and mark, its model, effort and per-tab choices, its token counts, which machine used it last and when, what project it belongs to (the git remote, the branch and the folder inside the repository), and which changed files you ticked as reviewed.
- A copy of Claude Code's transcript, with the subagents' transcripts.
- A usage file, while you [share usage](../usage/sharing-usage-across-machines.md): each machine's turns in the session, counts only.

The library also holds the file that [settings sync](settings-sync.md) uses, a `usage` folder with a file for each machine that shares its plan usage, and a `scratch` folder with the [scratch pads](../conversation/the-scratch-pad.md) of projects that have a git remote.

Claude Code's sign-in and settings are never copied.

Claudette writes each file under a temporary name and renames it when it's whole, so a sync client never uploads half a file.

## Naming this machine

Settings → Sessions → **Name for this machine** is the name History shows for a session another machine used last, and the name in notes like *in use on DESKTOP-01*. Leave it empty to use the computer's name. Give your machines names you'll tell apart.

## Keeping sessions

Claude Code deletes its own transcripts after 30 days by default. The library's copies aren't affected, so for the sessions that sync, the library is also a longer-term archive.

Settings → Sessions → **Keep library sessions for** says how long: **1 month**, **1 year** or **Forever**. Forever is the default. Claudette removes older copies when it starts.

It never removes a session that's open in a tab that syncs, or one that another machine is using. A library copy of a tab that no longer syncs is removed like any other.

## Settings

| Setting | Default | Where |
|---|---|---|
| Session library folder | Inside the data folder | Settings → Sessions → **Session library folder** |
| **Name for this machine** | The computer's name | Settings → Sessions |
| **Keep library sessions for** | **Forever** | Settings → Sessions |
| **Sync new tabs to the session library** | Off | Settings → Sessions, explained in [Syncing a tab](syncing-a-tab.md) |

These settings, like the library's folder, stay on each machine. **Reset to defaults** on the page leaves the library folder and settings sync as they are, since changing either moves where your sessions and settings live.

## See also

- [Syncing a tab](syncing-a-tab.md) for choosing which sessions go into the library.
- [Where Claudette keeps things](../privacy/where-claudette-keeps-things.md) and [What leaves this computer](../privacy/what-leaves-this-computer.md) for the whole picture of your data.
