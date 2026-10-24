---
title: The diff view
parent: Reviewing changes
nav_order: 2
summary: "The diff view shows what Claude changed in one file: the file as it was before Claude's first change in the session, against the file as it is now."
---

# The diff view

The diff view shows what Claude changed in one file: the file as it was before Claude's first change in the session, against the file as it is now. It's a window of its own, so it can stay open beside the conversation.

{% include shot.html name="diff-standard" alt="The diff view window for StarfallCharacter.cpp, with the changed lines marked and a Revert button on each change." %}

## Opening it

Click a file in [Changed files](changed-files.md), or select it and press `Enter`. The line under the title says what the file is compared with: its contents before Claude's first change in the session, or, with **Working tree vs HEAD** on, HEAD.

## Reading it

- The view shows the changes with a few lines of context between them. **Whole file** shows every line.
- Inline shows one column of lines with the old ones marked `−` and the new ones `+`. **Side by side** puts the old file on the left and the new one on the right.
- Lines are colored by the file's language, in the app's light or dark theme. Files over 20,000 lines and binary files are shown without colors.
- Long lines scroll sideways, with the scroll bar along the bottom, `Shift` and the mouse wheel, or a touchpad. Both sides scroll together, and the line numbers stay where they are.
- **Wrap lines** wraps long lines at the width of the window instead, each side in its half when they're side by side, so there's nothing to scroll sideways. The next diff you open, in its window or a [Files tab](../project-files/claudes-changes-in-the-editor.md), starts the way you left it.
- A byte order mark at the start of a file isn't counted as a change.
- A binary file says there's nothing to show line by line, and a file that's gone says it has been deleted.

When Claude changes the file while the window is open, choose **Refresh** in the **…** menu to read it again.

## The file's actions

The buttons along the top:

- **Side by side**, **Whole file** and **Wrap lines** switch how the changes show.
- **Open in diff tool** appears when you've set an [external diff tool](external-diff-tools.md).
- **Open** opens the file, as described below.
- **Revert file** puts the whole file back ([Reverting](reverting.md)). Each change has a **Revert** of its own on its header.
- **Reviewed** ticks the file and closes the window ([Reviewed files](reviewed-files.md)).
- **…** has **Open in the OS's app**, **Reveal in folder**, **Copy path** and **Refresh**.

The same actions are on the right-click menu of a row in Changed files.

## Opening the file

**Open** opens the file in Claudette's own editor ([Project files](../project-files/index.md)), unless Settings → **Files and diffs** → **Open files in** is set to **The app the OS uses for the file**. When the file opens in Claudette's editor, the diff window closes, so it doesn't hide the editor behind it. **Open in the OS's app** is always there when you want the app your system uses for that kind of file, such as an editor you can read with a screen reader.

Settings → **Files and diffs** → **Open in the diff window** can send **Open** elsewhere:

- **Opens the file, as Open files in says**: the default, described above.
- **Opens the file in the app the OS uses for it**, whatever **Open files in** says. The diff window stays open.
- **Opens the changes in the diff tool**: the same as **Open in diff tool**, once you've set an [external diff tool](external-diff-tools.md). A file whose earlier contents aren't known opens as **Open files in** says, since the tool would have nothing to compare.

## A file whose earlier contents aren't known

The view shows the file as it is now, with nothing marked as changed, and says that what it held before isn't known. It opens in the built-in view even when an external diff tool is set, since a tool would have nothing to compare it with.

## Settings

- Settings → **Files and diffs** → **Open files in**: where **Open** goes ([Opening files from the conversation](../project-files/opening-files-from-the-conversation.md)).
- Settings → **Files and diffs** → **Open in the diff window**: whether the diff window's **Open** goes there, to the OS's app, or to your diff tool ([Opening the file](#opening-the-file)).
- Settings → **Files and diffs** → **Diff tool**: which program opens a changed file's diff ([External diff tools](external-diff-tools.md)).

## See also

- A turn's own diffs, which are read-only, are in [Changes per turn](changes-per-turn.md).
- In a Files tab, a changed file shows the same diff inside its tab ([Claude's changes in the editor](../project-files/claudes-changes-in-the-editor.md)).
