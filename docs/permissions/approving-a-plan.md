---
title: Approving a plan
parent: Permissions and prompts
nav_order: 4
summary: "In Plan mode, Claude looks around and writes a plan instead of changing anything, then asks you to approve it before it starts."
---

# Approving a plan

In Plan mode, Claude looks around and writes a plan instead of changing anything, then asks you to approve it before it starts. Plan mode is Claude Code's; see its [common workflows](https://code.claude.com/docs/en/common-workflows) for what it's for. To use it in a tab, pick **Plan** in the mode picker ([Permission modes](permission-modes.md)).

## The plan card

When Claude has finished planning, a card titled *Claude's plan* shows the plan as formatted text, and the tab waits on you. If Claude sent no plan text, the card says *Claude has finished planning and is ready to start making changes.*

Choose how Claude carries on:

- **Approve, auto mode** leaves Plan mode for Auto. It's the main button while Auto is offered ([when it isn't](permission-modes.md#when-auto-isnt-offered)).
- **Approve, accept edits** leaves Plan mode for Accept edits: Claude edits files without asking. It's the main button when Auto isn't offered.
- **Approve, ask before edits** leaves Plan mode for Manual: Claude asks before each edit.
- **Keep planning…** sends the plan back. A box asks *What should change in the plan? (optional)*; **Keep planning** sends your feedback and **Cancel** goes back. Claude is told not to change anything yet.

The card then says what you chose, such as *Approved. Auto mode checks actions and blocks risky ones* or *Kept planning: use the existing input class*. The tab's mode changes to match, and the picker shows it.

From the keyboard, `Ctrl/Cmd+Enter` chooses **Approve, ask before edits**, the cautious one, and `Ctrl/Cmd+Backspace` chooses **Keep planning…** without feedback.

## Versions

Each plan Claude puts up in a session is a version. From the second on, the card's title says which: *Claude's plan · v2*. **Changes from v1** shows the plan as a line diff against the version before, in the colors of an edit's diff, so you can review a revision by what changed. Click it again to go back to the plan itself.

## Following the plan

Every version is kept on the Tasks page, with what became of it, and the tab's progress shows in the status line and the sidebar. [The Tasks page](../plans/the-tasks-page.md) explains these, and how to use a reply Claude wrote as the plan when it wasn't written in Plan mode.

## See also

- [Questions](questions.md): the other card where Claude asks you to choose.
- [The Tasks page](../plans/the-tasks-page.md): the plan's versions, and the tasks Claude makes from it.
