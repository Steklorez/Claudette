---
title: Model, effort and output style
parent: The composer
nav_order: 7
summary: "Two buttons on the left of the composer bar show which model the tab uses and how hard it thinks."
---

# Model, effort and output style

Two buttons on the left of the [composer bar](the-composer-bar.md) show which model the tab uses and how hard it thinks. They always show what the session is actually using, including changes you made with `/model` or another slash command.

The list of models and each model's effort levels come from Claude Code, not from a list built into Claudette. [Claude Code's model configuration](https://code.claude.com/docs/en/model-config) describes them.

## Switch the model

1. Click the model, shown by its name and ▾. A menu lists the models, each with a description.
2. Pick a different one. A question appears, **Switch model?**
3. Choose **Switch model**, or **Cancel**.

The question warns that *switching models resets this tab's cached context*: the new model has to read the whole conversation again, so your next message uses more of your limits. If the new model doesn't support the tab's current effort, the question says so, and the effort goes back to that model's default.

The conversation itself is kept. Claude Code switches the model in place, even in the middle of a turn.

## Set the effort

Click the effort, such as **High ▾**, and pick a level. The menu offers the levels the current model supports, and **Default**, which leaves the choice to Claude Code. It applies from the next turn, with no question.

## Ultracode

At the foot of the effort menu, **Ultracode** is a switch. With it on, Claude can run workflows of subagents on its own for big tasks, at the tab's effort. It uses more of your plan, as the switch's description says. See [Claude Code's workflows](https://code.claude.com/docs/en/workflows).

It applies from the next turn, is kept with the tab and is switched on again whenever the tab's Claude Code starts. The effort button says so: *High · Ultracode*.

## Output style

Under **Ultracode**, **Output style, for this folder** lists the styles the session offers, with the current one in bold: **Default**, **Explanatory**, **Learning** and any you've made. Pick one and it applies from Claude's next reply. [Claude Code's output styles](https://code.claude.com/docs/en/output-styles) says what each does.

The choice is kept in the folder's own Claude Code settings, where `/output-style` keeps it, so a terminal session in the same folder uses it too. A tab started without the folder's own settings, through **Start without it** on the folder trust question, doesn't offer styles, because that's where the choice is kept ([Folder trust](../permissions/folder-trust.md)).

## A fallback model

Settings → Claude Code → **Fallback model** is none by default. Choose one and tabs start with it as the model to switch to when their own is overloaded, rather than waiting. It applies to tabs started after you change it.

## What new tabs start with

Settings → New tabs → **Default model** and **Default effort** set what a new tab starts with. For one tab, **Tab settings…** has **Model** and **Effort** ([Tab settings](../tabs/tab-settings.md)).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Default model** | Default | Settings → New tabs |
| **Default effort** | Default | Settings → New tabs |
| **Fallback model** | None | Settings → Claude Code |

The tab's own model and effort, and **Ultracode** and **Output style**, are on the tab, as above.

## See also

- [Permission modes](../permissions/permission-modes.md), the third choice on the bar.
- [The prompt cache](../usage/the-prompt-cache.md), which a model switch forgets.
