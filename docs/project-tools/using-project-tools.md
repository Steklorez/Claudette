---
title: Using project tools
parent: Project tools
nav_order: 1
summary: "A tab can do things for the project in its folder: launch the editor, generate project files, build, open the solution."
---

# Using project tools

A tab can do things for the project in its folder: launch the editor, generate project files, build, open the solution. Which things you get depends on the project. Claudette knows [Unreal Engine](unreal-engine.md), [Unity](unity.md) and [Godot](godot.md) projects, and a folder can have its own [actions and links](actions-and-links.md). [Perforce](perforce.md) has a page of its own.

{% include shot.html name="guide/project-tools/project-page" alt="The Project page in the side panel for the Unreal project, with the project's details." %}

## The project row

At the foot of the sidebar, above **History**, a row shows the selected tab's project: a cube icon, its name and its version, such as `NightOwl · UE 5.4 ›`. It's there for every tab, whatever the folder holds, so you can add a first action or link from it. When no project is recognized, the row shows the folder's name, and its tooltip has the folder's path.

While a job runs, a busy dot replaces the icon and the job's name replaces the project's. When the sidebar is collapsed to its rail, the row is the icon alone.

Click the row to open its menu. From the top, it has:

- A header with the project's name and kind, the engine's version and folder, or what's wrong ("The engine for EngineAssociation "5.9" wasn't found on this machine"). Without a project, it shows the folder's name and path.
- Notes about entries in `claudette.json` that were skipped, and why.
- **Projects in this folder**, when there are several, as radio items. Your pick is remembered for that folder on this machine.
- The project's actions.
- The project's choice, such as Unreal's **Editor configuration**.
- The project's fix, such as **Choose engine folder…**.
- Your own actions, then your **Links**. See [Actions and links](actions-and-links.md).
- **Show in Explorer** (**Show in Finder** on macOS, **Show in File Manager** on Linux), which opens the project's folder, **Show output…**, which shows the newest run's log once an action has run, **Add an action…**, **Add a link…** and **Refresh**.

An item that's greyed out says why in its tooltip, for example "Generate project files first". The menu looks at the project's files again each time it opens, so it's current. **Refresh** does the same on demand.

## How a project is found

When a tab opens, when its folder changes, when the menu opens and on **Refresh**, Claudette looks for a project in the tab's folder, in its subfolders two levels down (for `Game/NightOwl/NightOwl.uproject`), and in the parent folders up to the repository's root. It skips generated and hidden folders such as `Intermediate`, `Library`, `node_modules` and `bin`. With several projects, the nearest comes first, and the menu's **Projects in this folder** lets you pick another.

## Running an action

An action is one of four kinds:

- **Launch** starts a program that keeps running on its own, such as the editor. Claudette doesn't track it.
- **Run** is a job, such as a build. It gets a run under the tab's row, with its own log.
- **Open** opens a file or folder in the OS's app, or a solution in the IDE you chose in Settings.
- **Destructive** actions, such as deleting folders or ending editors, ask you first.

One job runs at a time per tab. The other jobs stay greyed out until it ends. Jobs and launches get your own environment, so a build finds the same tools a terminal does ([Command line and environment](../reference/command-line-and-environment.md)).

## Runs

Each job is a run. It shows under its tab's row in the sidebar, newest last, with a glyph for how it's going: the busy dot while it runs, a check mark when it succeeded, a red cross when it failed and a muted square when you stopped it. Under its name, it says how long it has run (*Running · 1m 05s*) or how it ended (*Failed · exit code 6 · 14:32*).

- Click a run to show its log where the conversation is (below). Starting a run doesn't take you away from the conversation.
- A running one has **Stop** in the place of the × button, so a stray click never ends a build. A finished one has ×, which takes the run and its log away.
- Right-click a run for **Open beside**, **Close pane**, **Copy output**, **Stop** while it runs, and **Close** once it's done.
- Runs stay until you close them, whatever the result. Closing the tab takes its runs away, and they aren't saved when Claudette quits or restarts.
- Launch and Open actions start a program with no output, so they don't make runs.

## A run's log

A run's log shows in the main area, in the conversation's place, without the message box, so a build's errors have room. Click the tab's own row to go back to the conversation.

- At the top: how the run went (running, succeeded, failed with its exit code, or stopped), when it started and how long it took, with **Stop**, which ends the job and every process it started, **Copy**, which copies the whole log, and **Close** once it's done.
- Under it, the output, following the newest line until you scroll up, and again once you scroll back to the end. Long lines wrap. A run keeps its last 5,000 lines, and the log says how many older ones were dropped.
- Select text and copy it as anywhere else, and `Ctrl/Cmd+F` finds in the log.
- **Links.** Web addresses open in your browser. Files and folders the log names open with a click, as files Claude names do: in Claudette's [editor](../project-files/opening-files-from-the-conversation.md) at the line an error points to, such as `Source/Player.cpp(42,7)` or `src/main.rs:42:7`, or in the OS's app, as Settings → Editor says. A path is a link only once Claudette finds it, from the folder the action ran in or the tab's, so text that only looks like a path stays plain. A program or script shows in its folder rather than running.
- To see the log and the conversation at once, right-click the run and pick **Open beside**, or drag the run onto the conversation. See [Split panes](../tabs/split-panes.md).
- `Ctrl/Cmd+W` closes a finished run's log. On a running one, it goes back to the tab, so it never stops a build.

## The Project page

The **Project** page of the side panel, beside Changed files, Agents and Processes, shows the project's details: its file, the engine's version, folder and kind, the editor target and the configuration. It also lists entries of your project files that were skipped. The runs' logs are in the main area.

The page's tab shows a busy dot while a job runs. It's there when the tab has project tools: a project, actions of your own, or skipped entries. A running job's processes also show on the [Processes](../tabs/processes.md) page when the process monitor is on. Closing the tab stops a running job along with its other processes.

## Run the project's main action

`Ctrl/Cmd+Shift+E` runs the project's main action: **Launch editor** for Unreal, **Open in Unity** for Unity, **Open in Godot** for Godot, and otherwise the folder's first action of your own. You can rebind it in Settings → Keyboard.

## Telling Claude about the project

For Unreal, Unity and Godot projects, Claudette adds a short note to the session when a tab starts: the engine's version and folder, how to build or check the project without the editor, and not to open the editor unless asked. Each engine has its own switch in Settings → Project tools, on its page. The tab's [info card](../tabs/the-tab-info-card.md) has a **Project** row that says whether Claude was told.

## A project action finishes

When a job finishes or fails while Claudette isn't in front, a notification says so: *"Build editor failed (exit code 6)."* A job you stopped doesn't notify. Click it to see that run's log. The switch is Settings → Notifications → **A project action finishes**, on by default. See [Notifications](../away/notifications.md).

## Settings

Settings → Project tools holds the defaults every project starts with. They stay on this machine and don't sync. Each is explained on its engine's page:

- Unreal Engine: **Default editor configuration**, **Project files for**, **Open solutions with** and **Tell Claude about Unreal projects**. See [Unreal Engine](unreal-engine.md).
- Unity: **Default code optimization** and **Tell Claude about Unity projects**. See [Unity](unity.md).
- Godot: **Godot executable** and **Tell Claude about Godot projects**. See [Godot](godot.md).
- **Show editor logs under the tab**: the rows for the logs the editor writes. See [Editor logs](editor-logs.md).

Below the categories in the Settings window, a group headed by the selected tab's project has three pages: **Links**, **Actions** and **Tools**. The Settings window follows the tab that was selected when it opened, and each page says which in a line at the top.

The **Tools** page shows this machine's choices for the tab's project, the same ones the project's menu makes:

- The project, when the folder has several.
- The project's own choice: Unreal's editor configuration, or Unity's code optimization.
- What you picked with **Choose…**: Unreal's engine folder, the Unity editor or the Godot executable. It shows what's in use now, and whether you chose it or Claudette found it. **Clear** forgets your pick so Claudette looks again.

Without a project, the page says "No Unreal, Unity or Godot project in this folder" and links to Settings → Project tools. These choices are remembered on this machine, keyed by the project, and don't sync. **Reset to defaults** on the Project tools page leaves them alone.

## See also

- [Actions and links](actions-and-links.md) for your own commands and web pages.
- [The sidebar](../tabs/the-sidebar.md) for the rest of a tab's row.
- [The Settings window](../settings/the-settings-window.md) for how the project's pages sit in it.
