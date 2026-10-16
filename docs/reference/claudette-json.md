---
title: claudette.json
parent: Reference
nav_order: 5
---

# `claudette.json`

The format of the two files that hold a folder's own actions and links. [Actions and links](../project-tools/actions-and-links.md) shows how to add them from the app and what they do.

Both files live in the tab's folder, and only there: Claudette doesn't look for them in parent folders.

- `claudette.json` is shared. Commit it with the project.
- `claudette.local.json` is yours alone. Put it in `.gitignore`. It has the same shape, and its entries come after the shared file's.

A missing file means nothing, and so does an empty one. Claudette's own repository has a `claudette.json` at its root, with a link to its issues and actions that pull and rebuild it.

## An example

```json
{
  // Comments and trailing commas are fine.
  "actions": [
    { "name": "Run tests", "command": "dotnet test", "folder": "src", "mode": "output" },
    { "name": "Open Grafana", "command": "start https://grafana.example", "mode": "launch", "os": ["windows"] },
    { "name": "Play the build", "command": "Build\\Game.exe", "mode": "launch", "ifExists": "Build/Game.exe" },
    { "name": "Install packages", "command": "npm ci", "runOnNewWorktree": true },
  ],
  "links": [
    { "name": "Board", "url": "https://example.atlassian.net/jira/software/projects/ABC/boards/1" },
    { "name": "Pull request", "url": "https://github.com/org/repo/compare/{branch}?expand=1" }
  ]
}
```

The top level is an object with two lists, `actions` and `links`. Other keys are ignored, and kept when Claudette saves the file from Settings.

## Actions

Each entry of `actions` is an object. It shows in the project's menu after the project's own actions, under a separator, in the order of the files and then of the list.

| Field | Required | What it is |
|---|---|---|
| `name` | Yes | The label in the menu. |
| `command` | Yes | The command line. It runs through your shell: `cmd.exe` on Windows, and your `$SHELL` (or `/bin/sh`) on macOS and Linux. |
| `folder` | No | The folder to run in, relative to the tab's folder. The tab's folder by default. If it doesn't exist, the action is greyed out and says so. |
| `mode` | No | `"output"`, the default, makes a run with its log on the Project page, **Stop** and a notification when it ends. `"launch"` starts the command and leaves it alone. |
| `os` | No | `"windows"`, `"macos"` or `"linux"`, or a list of them. The action shows only on those systems, with no reason given elsewhere. `win`, `win64`, `mac`, `osx` and `darwin` also work, in any case. |
| `ifExists` | No | A path, or a list of paths, relative to the tab's folder. The action shows only while every one exists, as a file or a folder, and is left out without a reason otherwise. Empty paths are ignored. |
| `runOnNewWorktree` | No | `true` also runs the action on its own in each new worktree a worktree tab makes. Anything but the value `true`, a quoted `"true"` included, leaves it to a click. |

`ifExists` is checked each time the files are read, so an action that runs a build appears once a build has made it.

## Links

Each entry of `links` is an object.

| Field | Required | What it is |
|---|---|---|
| `url` | Yes | The address to open. |
| `name` | No | The label in the menu. Without one, the menu shows the address. |

Only `https`, `http` and `mailto` addresses open, and an address has to be a full one, such as `https://example.com`. A link with another kind of address, or with a placeholder that can't be filled, isn't skipped: it's listed greyed out, and its tooltip says why.

### Placeholders

An address can have these placeholders. They're filled in from the selected tab and escaped for a web address. The names aren't case-sensitive.

| Placeholder | Filled with | When it can't be filled |
|---|---|---|
| `{branch}` | The tab's git branch | The link is greyed out: "No git branch" |
| `{changelist}` | The Perforce changelist Claude is working in | "No Perforce changelist" |
| `{folderName}` | The name of the tab's folder | It's always there |

Any other word in braces makes the link greyed out: "Unknown placeholder … : use {branch}, {changelist} or {folderName}".

## What's skipped

Claudette reads both files tolerantly:

- Comments (`//` and `/* … */`) and trailing commas are fine, and so is a byte order mark at the start.
- Unknown fields are ignored.
- An entry it can't use is skipped on its own, with a reason that the project's menu and the Project page show, such as "claudette.json: actions[2] has no command, so it was skipped." The rest of the file still works.
- A file that isn't JSON, or whose top level isn't an object, is skipped whole, with the parser's reason: "claudette.json isn't valid JSON, so it was skipped: …". A file that can't be read at all, because it's locked or you can't open it, says so too.

These skip an entry:

| Entry | Skipped when |
|---|---|
| An action | It isn't an object, or has no `name`, or no `command`. `name` and `command` have to be text. |
| An action | `mode` is anything but `"output"` or `"launch"`. |
| An action | `os` or `ifExists` isn't a string or a list of strings. |
| A link | It isn't an object, or has no `url`. |
| Either list | `actions` or `links` isn't a list: "\"actions\" should be a list, so it was skipped." |

An action for another system or with a missing `ifExists` path isn't skipped with a reason. It just isn't shown.

## When Claudette reads them

Claudette doesn't watch the files. It reads them again when you select the tab, when Claudette comes to the front, when a turn ends, when the project's menu opens and after Settings saves. **Refresh** in the menu does it on demand.

When you edit entries in Settings, saving replaces only the list you edited, keeps every other key and each entry's other fields, such as `os`, and writes the file indented. **Comments are dropped**, because JSON can't keep them. A file that isn't valid JSON is never overwritten: Settings shows the reason and asks you to fix the file by hand first.

## See also

- [Actions and links](../project-tools/actions-and-links.md), for adding them in the app.
- [Command line and environment](command-line-and-environment.md), for `CLAUDETTE_WORKTREE` and `CLAUDETTE_MAIN_CHECKOUT`.
