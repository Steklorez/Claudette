---
title: Git
parent: Settings
nav_order: 6
summary: "Settings → Git, the first page under Source control, holds the branch on tab rows, cleaning up worktrees, the git Claudette runs, and what the Files tab shows of git."
---

# Git

{% include unreleased.html %}

Settings → **Git** is the first page under **Source control** in the Settings sidebar, before [Perforce](../project-tools/perforce.md). It gathers what Claudette does with git:

- **Tab rows**: **Show the git branch**, the branch badge on each tab's row. See [Styles and themes](styles-and-themes.md#what-the-tab-rows-show).
- **Worktrees**: removing worktrees no tab works in, and **Clean up now**. See [Worktree tabs](../tabs/worktree-tabs.md).
- **Git**: **Path to git**, below.
- **Project Files tab**: **New Files tabs show**, which [Claude's changes in the editor](../project-files/claudes-changes-in-the-editor.md#what-the-changes-are-against) explains, and **Files git ignores**, which [The explorer](../project-files/the-explorer.md#the-tree) explains.

Everything but **Show the git branch** stays on this machine; Settings marks those sections *This computer*.

## Path to git

Claudette runs git to ask about your repositories: for Changed files and the diff view, worktree tabs and cleaning them up, the `@` list, what the Files tab dims, and a source build's new commits. It also runs it as the git difftool, when that's your [diff tool](../changes/external-diff-tools.md).

By default it runs the first `git` on your `PATH`, as a terminal would. To use another, type its path in **Path to git** or click **Browse…**. It's used from the next time Claudette runs git.

Under the box, Settings says which git it found and its version, such as *git 2.47.1.windows.2 at C:\Program Files\Git\cmd\git.exe*. When it can't use the one you named, it says why instead: there's no file at that path, it's a folder, or it isn't git.

Claude's own git commands, which it runs in its Bash tool, use the git Claude Code finds, whatever this says.

## See also

- [Every setting](every-setting.md#git)
- [The Settings window](the-settings-window.md)
