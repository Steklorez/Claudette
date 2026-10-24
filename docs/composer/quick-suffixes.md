---
title: Quick suffixes
parent: The composer
nav_order: 6
summary: "Saved snippets of instructions you add to a message in a click, such as asking Claude to ask questions first."
---

# Quick suffixes

A quick suffix is a saved snippet of instructions, such as *Ask clarifying questions before you start.*, that you can add to a message in a click. It appears as a chip under the message box instead of being pasted into your text, so the message stays easy to edit.

## Add a suffix to a message

1. Click **Suffixes ▾** in the bar, or press `Ctrl/Cmd+Shift+S`.
2. Pick one from the menu. The first nine can be picked with the keys `1` to `9`.

The suffix shows as a chip under the box. You can add several. A check mark in the menu is beside each suffix already on the message, and picking a checked one takes it off again. So does the `×` on its chip.

When you send, the suffixes are added in the order shown, after a blank line. The sent message in the conversation shows the full text, with the suffix part in a lighter style. A message can be only suffixes.

### After a slash command

Claude Code takes everything after a command's name as its arguments, so a message that starts with `/` puts its suffixes in a block of their own before the command. A command that runs a prompt, such as a skill or a custom command, gets them alongside that prompt, and its arguments stay what you typed. A local command such as `/compact` or `/model` doesn't ask the model anything, so its suffixes change nothing.

## Keep a suffix on

Right-click a chip and choose **Keep on this tab** (**Stop keeping on this tab** on a chip that's kept). A kept suffix is added to every message in the tab until you turn it off, and its chip reads, for example, *Plan only (kept)*. Kept suffixes are saved with the tab.

## Give a suffix its own shortcut

A suffix can have a shortcut of its own that adds it directly. Set it in Settings → Keyboard → **Quick suffixes**, below.

## Manage your suffixes

Settings → Keyboard → **Quick suffixes** lists them. **Edit suffixes…** at the foot of the **Suffixes ▾** menu opens it.

- **Add suffix** adds one. Give it a label, which shows in the menu and on the chip, and the text that's added to the message.
- The arrows ↑ and ↓ move a suffix, which also sets its number in the menu. **Delete** removes it.
- **Shortcut**: click the button, then press the keys. **Remove** clears it. Claudette refuses a shortcut another command or suffix already uses.
- The section's **Reset to defaults** replaces the list with the five built-in suffixes. Your own are removed.

### The built-in suffixes

You can edit or delete these too.

| Label | Text |
|---|---|
| Clarify first | Ask clarifying questions before you start. |
| Plan only | Don't change any files yet. Explain your plan and wait for me to approve it. |
| Keep it small | Keep the change as small as possible and don't refactor unrelated code. |
| Test it | Run the relevant tests when you're done and fix any failures. |
| Explain | Explain what you changed and why when you're done. |

## Settings

| Setting | Default | Where |
|---|---|---|
| The suffixes, their labels, texts and shortcuts | The five above | Settings → Keyboard → **Quick suffixes** |
| **Quick suffixes menu** | `Ctrl/Cmd+Shift+S` | Settings → Keyboard |

## Shortcuts

| | Windows and Linux | macOS |
|---|---|---|
| Open the **Suffixes ▾** menu | `Ctrl+Shift+S` | `Cmd+Shift+S` |
| Pick one of the first nine in the menu | `1` to `9` | `1` to `9` |

## See also

- [Earlier prompts, drafts and the stash](earlier-prompts-drafts-and-the-stash.md): a suffix added for one message is part of the draft; a kept one isn't.
- [Settings: Keyboard](../settings/keyboard.md), for changing shortcuts.
