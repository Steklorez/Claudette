---
title: Attachments and large pastes
parent: The composer
nav_order: 4
summary: "You can give Claude images and files by dropping them on the message box, pasting them or picking them with the paper clip."
---

# Attachments and large pastes

You can give Claude images and files by dropping them on the message box, pasting them or picking them with the paper clip. Text too long to sit comfortably in the box is kept as a chip above it.

## Attach something

- **Drop** one or more files or images on the message box.
- **Paste** with `Ctrl+V` or `Shift+Insert` (`Cmd+V` on macOS), or **Paste** on the box's right-click menu.
- **Pick** with the paper clip (**Attach a file**) at the left of the bar.

While you drag a file over the box, Claudette holds her arms up to catch it. See [Claudette on the composer](../claudette/claudette-on-the-composer.md).

## Images

PNG, JPEG, GIF and WebP images work. Claudette goes by what's in the file, not its name.

- Each shows as a small thumbnail above the text, with a `×` (**Remove image**) to take it off.
- They're sent before your text. A message can be only images.
- Each image can be up to 20 MB, and a message can have up to 20. Past either limit a note under the box says why, such as *Pasted image is 20.1 MB; images can be up to 20 MB.*, and the image isn't attached. The note has a `×` to dismiss it.
- Claude Code scales images down itself before they reach the API, so a large one is fine within the limit. Claudette sends them as they are.

A sent message shows its images as thumbnails above its text. They come back with a restored tab.

## Files and folders

Files and folders aren't uploaded. They become `@path` mentions at the caret, relative to the working folder when they're inside it and a full path otherwise, and Claude Code reads them when the message is sent. Text files, PDFs and notebooks work. See [Slash commands and mentions](slash-commands-and-mentions.md).

A file Claude can't read, such as an executable, and an image in another format, such as BMP, are refused with a note under the box: *Claude can't read app.exe. Attach text files, PDFs, folders, or PNG, JPEG, GIF or WebP images.*

## Paste

- Copied **files** are attached.
- **Text** pastes as text, even when the clipboard also holds a picture of it, because many apps add one. Text that is only white space doesn't count: some apps add a line break to a copied picture.
- An **image** on its own, such as a screenshot, is attached. On Windows that includes what Snipping Tool and `Win+Shift+S` copy. On macOS and Linux, PNG and JPEG images on the clipboard work, and on macOS TIFF too.

A pasted or dropped picture is sent as PNG. When the PNG would be over 20 MB, as a photo-like screenshot of a large screen can be, it's sent as a JPEG instead.

## Large pastes

Text over 32 KB, such as a log, isn't put in the box, where it would make typing slow and the message hard to edit around. It becomes a chip above the box, such as *Pasted text · 1,204 lines · 48 KB*. Hover the chip to see its first 12 lines. Its `×` (**Remove pasted text**) removes it.

- A large paste is sent after what you typed, each paste after a blank line. Claude reads all of it, and an `@path` in what you typed is still read.
- A message can be only large pastes.
- `Ctrl/Cmd+Shift+V` pastes the clipboard's text into the box as it is, however long.
- When you cancel a [queued message](sending-and-stopping.md#messages-sent-while-claude-works), its pastes come back as chips.

In the conversation, a long message of your own, over 4,000 characters or 40 lines, shows its start with **Show all (48 KB)**. Find opens it when the match is in the part that's hidden ([Finding your way](../conversation/finding-your-way.md)).

## Attachments and drafts

Images and large pastes stay with the message until you send it. If you quit first, they're kept with the tab's draft ([Earlier prompts, drafts and the stash](earlier-prompts-drafts-and-the-stash.md)).

## Shortcuts

| | Windows and Linux | macOS |
|---|---|---|
| Paste | `Ctrl+V` or `Shift+Insert` | `Cmd+V` |
| Paste text as it is, however long | `Ctrl+Shift+V` | `Cmd+Shift+V` |

## See also

- [Images](../conversation/images.md), for the pictures Claude sends back.
