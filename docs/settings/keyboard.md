---
title: Keyboard
parent: Settings
nav_order: 5
summary: "Settings → Keyboard sets which key sends your message and lists every shortcut Claudette handles."
---

# Keyboard

Settings → Keyboard sets which key sends your message and lists every shortcut Claudette handles. You can change any of them. [Keyboard shortcuts](../reference/keyboard-shortcuts.md) in the Reference lists them all with their defaults.

## Send with

The top of the page picks the key that sends the message in the composer, and the one that starts a new line:

- **Enter, with Shift+Enter for a new line** is the default.
- **Ctrl+Enter, with Enter for a new line** (**⌘↩, with Enter for a new line** on macOS) suits messages with many lines.

The composer's placeholder says which is in use. `Ctrl/Cmd+Enter` also answers a waiting prompt, as **Allow the waiting prompt** does: with it as the send key, it sends what you've typed, and with nothing typed it answers the prompt. [Sending and stopping](../composer/sending-and-stopping.md) explains the composer's side, and [Permission cards](../permissions/permission-cards.md) the prompts.

## Changing a shortcut

1. Open Settings → Keyboard.
2. Click the shortcut you want to change. It says "Press keys…".
3. Press the new keys. `Esc` cancels.

A command you've changed also has **Reset**, which puts its default back. **Remove** clears a command's shortcut, so it's reachable only from menus and buttons, and the page says "None". **Reset to defaults** at the foot of the page puts back every shortcut and **Send with**. Tooltips and the composer's placeholder always show the shortcuts in use.

## Shortcuts Claudette refuses

When the keys you pressed can't be used, the row says why and the old shortcut stays. Press other keys, or `Esc` to give up:

- **Another command already has them**, or a quick suffix does. The row names it: "Ctrl+T is already used by New tab."
- **A letter, digit or punctuation key with no Ctrl, Alt or Cmd**, since it would get in the way of typing: "Add Ctrl, Alt or Cmd, so typing still works." Shift alone doesn't help. `Esc`, `Tab`, `Enter`, `Backspace`, `Delete` and the function keys are fine on their own.

## Go to tab 1–9

This is one shortcut for all nine digits. To change it, press any digit from 1 to 9 with the modifier keys you want, and the other eight follow. A shortcut on a digit, `+` or `−` also answers to the number pad's key.

## One key for both systems

Claudette stores a shortcut with a primary modifier, which is Ctrl on Windows and Linux and Cmd on macOS. A shortcut synced between a Windows machine and a Mac then means the same thing on both. Only on macOS is Ctrl a modifier of its own, separate from Cmd. On macOS the page writes shortcuts with symbols, such as `⌘⇧H`.

## Quick suffixes

Each quick suffix can have its own shortcut, set in Settings → Quick suffixes and checked for conflicts the same way. [Quick suffixes](../composer/quick-suffixes.md) explains them.

## Keys that can't be changed

The page lists these under the shortcuts:

- In the composer, `Ctrl/Cmd+Shift+V` pastes text as it is, however long ([Attachments and large pastes](../composer/attachments-and-large-pastes.md)).
- In the new tab picker, `1` to `9` pick a folder. In the quick suffixes menu, they pick a suffix.

## The editor's keys

In a [Project Files](../project-files/index.md) tab, these keys also can't be changed:

| Action | Windows and Linux | macOS |
|---|---|---|
| Save the file | `Ctrl+S` | `Cmd+S` |
| Save every file | `Ctrl+Alt+S` | `Cmd+Option+S` |
| Replace | `Ctrl+H` | `Cmd+Option+F` |
| Go to a line | `Ctrl+G` | `Ctrl+G` |
| Wrap long lines | `Alt+Z` | `Option+Z` |
| Next file | `Ctrl+PageDown` | `Cmd+Option+→` |
| Previous file | `Ctrl+PageUp` | `Cmd+Option+←` |

## Settings

Both of these are in Settings → Keyboard, and both sync to your other machines when [settings sync](../history/settings-sync.md) is on.

| Setting | Default |
|---|---|
| **Send with** | Enter, with Shift+Enter for a new line |
| Each command's shortcut | Its default, listed in [Keyboard shortcuts](../reference/keyboard-shortcuts.md) |

## See also

- [Quick suffixes](../composer/quick-suffixes.md), whose shortcuts are set in their own page of Settings.
- [The command palette](../tabs/the-command-palette.md), which shows each command's shortcut beside it.
