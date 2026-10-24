---
title: External diff tools
parent: Reviewing changes
nav_order: 6
summary: "If you have a diff tool you prefer, Claudette can open a changed file's diff in it instead of the built-in diff view."
---

# External diff tools

If you have a diff tool you prefer, Claudette can open a changed file's diff in it instead of the built-in [diff view](the-diff-view.md).

## Choosing a tool

1. Open Settings → **Files and diffs**, or choose **Diff tool settings…** on a changed file's right-click menu.
2. Pick one from the **Diff tool** list:
   - **Built-in diff view**, the default.
   - A preset, listed with where Claudette found it.
   - **Custom command…**

Claudette finds presets by looking in each tool's usual install places and on your `PATH` (on macOS and Linux, your login shell's, as `claude` gets), and a preset is listed only if its tool is found. The presets are Beyond Compare, VS Code, WinMerge (on Windows), Kaleidoscope (on macOS), Meld, P4Merge and **Your git difftool**, which uses your git configuration.

A preset fills in the tool's arguments, including a title for each side (`auth.cs (before)` and `auth.cs (now)`), and opens the left side read-only where the tool supports it.

## A custom command

Choose **Custom command…** and write a program and its arguments in **Command**. These placeholders are filled in:

- `{left}`: the file before Claude's changes, a read-only copy;
- `{right}`: the file now;
- `{leftTitle}` and `{rightTitle}`: a title for each side.

For example, Beyond Compare on Windows:

```
"C:\Program Files\Beyond Compare 5\BCompare.exe" "{left}" "{right}" /title1="{leftTitle}" /title2="{rightTitle}"
```

If the command isn't valid, the page says why.

## Testing it

Click **Test** to open a sample diff in the tool. The page says *Opened a sample diff. If nothing appeared, check the command.*, or why it couldn't open.

## What changes once a tool is set

- Every changed file gets **Open in diff tool**, in its menu in [Changed files](changed-files.md) and in the diff view. A turn's changes, opened from its **Modified files** card, get it too ([Changes per turn](changes-per-turn.md#in-your-diff-tool)).
- Settings → **Files and diffs** → **Open in the diff window** can have the diff view's **Open** button open the tool too ([The diff view](the-diff-view.md#opening-the-file)).
- A double-click on a file in Changed files opens the tool, and a single click only selects it. The built-in view stays in the file's menu as **Open diff**.
- A file whose earlier contents aren't known always opens in the built-in view.

## What the tool gets

- The "before" side is a temporary read-only file in a folder of its own, with the file's name and extension (`auth (before).cs`), so the tool picks the right language.
- The "now" side is the real file in your folder, so edits you make in the tool are saved to it.
- The temporary files are deleted when the tab closes.

{: .note }
On Windows, a `.cmd` or `.bat` tool (VS Code's `code.cmd`, say) runs through `cmd.exe`, where a file name could run commands. Claudette refuses arguments that `cmd.exe` would read as commands, such as `%` or `"`, and says so. A file name that trips this, such as `Q&A.md` in a path without spaces, can still be opened in the built-in view.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Diff tool** | **Built-in diff view** | Settings → **Files and diffs** |
| **Command** | none | Settings → **Files and diffs**, with **Custom command…** |

**Reset to defaults** in the **Diff tool** section goes back to the built-in view.

## See also

- [Changed files](changed-files.md) for the list a double-click starts from.
