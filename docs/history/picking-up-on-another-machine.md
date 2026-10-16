---
title: Picking up on another machine
parent: History and syncing
nav_order: 4
---

# Picking up on another machine

A session that [syncs](syncing-a-tab.md) shows up in History on your other machines, where you can continue it. The library moves the conversation, not your code, so Claudette checks the folder and the code on this machine before it starts.

You need Settings → Sessions → **Session library folder** to be the same synced folder on both machines ([the session library](the-session-library.md)).

## Continuing a session

1. On the first machine, make sure the tab syncs and its turn has finished. Wait for your sync client to upload the library.
2. On the other machine, open [History](history.md). The session is there, with the first machine's name in its details.
3. Click it. Claudette finds the project, checks the code, and resumes the session in a new tab. From then on the tab syncs too.

Claudette copies the session to a working folder on this machine and resumes it from there, so Claude Code never writes into your synced folder in the middle of a turn. After each turn it copies the session back.

## Finding the folder

Folder paths differ between machines, so a session records its project by its git remote, its branch and the folder inside the repository. Claudette looks for a matching folder among the folders it remembers, your recent and favorite folders and your open tabs, and prefers a clone on the recorded branch. Then it tries the recorded path, in case it exists here too.

If it finds nothing, it asks *Where is the folder for "…" on this machine?* and remembers your answer for next time.

## When the code may differ

If this folder is on another branch or commit than the other machine's was, or the other machine had changes it hadn't committed, Claudette warns you first, under the heading **The code here may be different**. It says where the session was last used, on which branch and commit, and what this folder has. Claude's earlier file changes may not be here.

Choose **Continue anyway**, or cancel, push or pull the code to get it in step, and open the session again.

## One machine at a time

A tab that syncs keeps a small claim on its session in the library, refreshed every minute, so two machines don't write the same session at once. A claim that hasn't been refreshed for 10 minutes counts as stale.

If you open a session from History that another machine is actively using, Claudette asks *"…" is open on DESKTOP-01*, and offers:

- **Open a copy**, which starts a new session that begins as a copy. Both go their own way.
- **Take over**, which continues the session here. The tab on the other machine becomes read-only the next time it syncs.

A tab that was taken over stops its Claude Code, says *This session was taken over by DESKTOP-01.*, and becomes read-only. It reads *This session continued on DESKTOP-01. The tab is read-only here; open it again from History to continue on this machine.* A tab restored at launch that finds its session was taken over while Claudette was closed does the same instead of starting.

A tab that doesn't sync ignores all of this.

## Conflict copies

Sync clients sometimes make a second copy of a file when two machines change it at once, such as `session (1).jsonl`. Claudette shows these in History as separate entries, with the copy's label after the name, and never merges them. Opening one starts it as its own session in a folder of its own, so it can't overwrite the original. It recognizes the copies Google Drive, OneDrive, Dropbox and Syncthing make.

{: .note }
A transcript remembers file paths from the machine that wrote it. Continuing a session recorded on Windows on a Mac, or the other way round, hasn't been tried on real machines yet, so check what Claude does with the first file it touches.

## See also

- [Closing and restoring](../tabs/closing-and-restoring.md) for a pinned tab whose session Claude Code has deleted, which resumes from its library copy when there is one.
- [Settings sync](settings-sync.md) for keeping your settings the same on both machines.
