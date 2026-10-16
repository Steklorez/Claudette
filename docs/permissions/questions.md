---
title: Questions
parent: Permissions and prompts
nav_order: 3
summary: "Claude sometimes stops to ask you something with options to pick from, instead of guessing."
---

# Questions

Claude sometimes stops to ask you something with options to pick from, instead of guessing. The question shows as a card in the conversation, and the tab waits on you: its row says *Needs your input*, and a [notification](../away/notifications.md) goes out if Claudette isn't in front.

## Answering

The card says *Claude has a question* (or *Claude has some questions*) and lists each question, with a short header over it when Claude gave one. Under each:

- Options to pick from, each with its description. A question that takes one answer says *Pick one* and has round buttons. One that takes several says *Pick any that apply* and has check boxes.
- An **Other** box, *Other: type your own answer*, for something the options don't cover.

Then:

- **Send answers** gives Claude your choices, once every question has an answer. Several choices to one question reach Claude joined with commas.
- **Dismiss** tells Claude you dismissed the questions without answering, so it asks again later or carries on with its best judgment.

The card then shows what you answered, or *Dismissed without answering*.

## From the keyboard

`Ctrl/Cmd+Enter` sends your answers once every question has one, and `Ctrl/Cmd+Backspace` dismisses the questions, as they allow and deny a [permission card](permission-cards.md). Several waiting prompts are numbered the same way as permission cards are.

## See also

- [Approving a plan](approving-a-plan.md) is the other card Claude Code asks you to answer with a choice.
