---
title: Saving
parent: Project files
nav_order: 7
summary: "Claudette doesn't save for you."
---

# Saving

Claudette doesn't save for you. A file with unsaved edits has a dot on its tab until you save it, but its edits are kept safe meanwhile.

## Saving a file

- `Ctrl/Cmd+S` saves the file showing, while the Files tab has the focus. Anywhere else, `Ctrl/Cmd+S` stays the composer's **Stash** ([Earlier prompts, drafts and the stash](../composer/earlier-prompts-drafts-and-the-stash.md)).
- `Ctrl/Cmd+Alt+S` saves every open file (`Cmd+Option+S` on macOS).
- While the Files tab has the focus, the [command palette](../tabs/the-command-palette.md) has **Save the file** and **Save all files**.

A save keeps the file's encoding, byte order mark and line endings. It writes to a temporary name and renames it over the file, so a crash can't leave half a file. A link is written through to its file, and on macOS and Linux the file keeps its permissions.

It writes only if the file is still what you opened or last saved. If something changed it since, it asks, as described under [When a file changes on disk](#when-a-file-changes-on-disk). If the file can't be written, a note over the editor says *Couldn't save* and why.

## Unsaved edits are kept

A file's unsaved text is copied to Claudette's data folder a second after you stop typing ([Where Claudette keeps things](../privacy/where-claudette-keeps-things.md)). So quitting Claudette, a crash, or a restart into a new build asks nothing and loses nothing: the file comes back with its edits and its dot. Saving a file, or choosing **Don't save**, removes the copy, and copies no open file has used for 30 days are removed at launch.

## Closing files with unsaved edits

- **Closing a file** asks first, in place: *Save PlayerController.cpp?*, with **Save**, **Don't save** and **Cancel**.
- **Closing the Files tab, or its group,** with unsaved files asks once, naming them, with **Save all**, **Don't save** and **Cancel**.

## When a file changes on disk

An open file is watched. It's also looked at again when a tab of the project changes it with a tool, when one finishes a turn, and when Claudette comes to the front. The change may be Claude's, a `git checkout`, or your other editor.

- **With no unsaved edits**, the file is read again where it was. The cursor, selection, scroll position and folds stay on their lines as near as they can, and Undo takes the change back as one step.
- **With unsaved edits**, a banner over the editor, in the caution colors, says *Claude changed PlayerController.cpp in refactor auth while you were editing it.* When none of the project's tabs changed it, it says *PlayerController.cpp changed on disk while you were editing it.* It offers:
  - **Compare** shows the file on disk against your edits, side by side.
  - **Keep mine** keeps your edits. Saving then writes over the file on disk without asking again.
  - **Use theirs** reads the file again. Undo can take that back.
- **A file that's gone**, deleted or renamed, gets a banner saying so: *PlayerController.cpp is gone from disk. Save to write it back.* The text stays. **Keep editing** closes the banner, and saving writes the file back.

## Settings

None. Saving works the same everywhere.

## Shortcuts

| Key | Does |
|---|---|
| `Ctrl/Cmd+S` | Save the file showing (while a Files tab has the focus) |
| `Ctrl/Cmd+Alt+S` | Save every open file (macOS: `Cmd+Option+S`) |

## See also

- [Claude's changes in the editor](claudes-changes-in-the-editor.md): **Revert** there changes the text and leaves it unsaved.
