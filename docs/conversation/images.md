---
title: Images
parent: The conversation
nav_order: 11
---

# Images

Claude doesn't make images, but it does see them, and the conversation shows them in two places: under the cards of tools that handed Claude an image, and inside a reply's Markdown. Images you attach to your own messages are in [Attachments and large pastes](../composer/attachments-and-large-pastes.md).

## Images from Claude's tools

A tool's result can hold images: a Read of an image file (PNG, JPEG, GIF or WebP), a PDF's pages read with `pages`, a notebook's figures, a Bash command whose output is an image, and an MCP tool's image, such as a screenshot from a browser tool. They show under the tool's tile without opening it, each at most 120 pixels high and 320 wide, never larger than it really is. Several images, such as a PDF's pages, wrap onto more lines. A restored tab shows them too.

Each image is named for what it is, as its tooltip: a Read of an image by its file's name, a PDF's page as *report.pdf, page 3*, a notebook's figure as *analysis.ipynb, figure 2*, anything else *Image* (or *Image 2* of several).

A whole PDF read without `pages` isn't images: the card says what was read.

### Showing one full size

Click an image to open it in a window of its own beside the conversation, titled with its name. It opens at its own size, up to 1400 by 900 pixels and 90% of the screen, and from then on any size you drag it to, shrinking to fit but never growing past its own size. `Esc` closes it.

Under the title are the tool, the type and size (*Read · PNG 245 KB*), and, when Claude Code shrank the image before sending it, *scaled down from 3000 × 1000 to send*, since what you see is what Claude saw.

- **Copy image** puts it on the clipboard and says *Copied* for a moment.
- **Save as…** saves it under its name, with its type's extension. Claude Code may have sent a large PNG on as a JPEG, which then saves as `.jpg`. A save that fails says why under the title.
- **Open**, for an image file Claude read, opens the file in the app your OS uses for it.

## Images in Claude's replies

A reply's Markdown can include an image. A reply can be steered by anything Claude read, such as a web page or a file in the project, and an image that loaded by itself could send data out in its web address. So Claudette loads only what can't reach another machine, and asks before the rest.

- **Right away.** A file in the tab's folder (the worktree, for a tab in one), as long as nothing on the way down from the folder is a link, which could lead anywhere; and an image embedded in the reply itself. A relative path is read from the folder, so a chart Claude just made shows up.
- **When you click.** An image from the web, or a file anywhere else, shows a placeholder: *Image from example.com, which loads only when you ask.* or *Image at /tmp/chart.png, outside the tab's folder.* Its button is **Load image** for the web and **Show image** for a file. The address is the placeholder's tooltip.
- **Never.** An image on a network path (`\\server\share\…`, `//server/…`), which would hand your credentials to that server, and any address that isn't a web address or a file. The placeholder says why, with no button.

An image comes through if it arrives within 30 seconds, is at most 20 MB and really is a PNG, JPEG, GIF or WebP, whatever its name says. If it fails, the placeholder says why (*Couldn't load it: example.com answered 404 Not Found.*) and the button stays, so you can try again.

An address you loaded once loads by itself in that tab from then on, until you close it. A loaded image is shown at most 480 pixels high and as wide as the conversation, never larger than it is. Click it to open it full size, in the same window as above.

This applies wherever Markdown shows: replies, plans, the Tasks page, and a subagent's prompt and report.

## Not in find or an export

Find and [Exporting](exporting.md) skip images, since they have no text to find. An export's line for a tool call keeps its summary.

## See also

- [Tool calls](tool-calls.md) for the tiles the images sit under.
- [Files and links in replies](files-and-links-in-replies.md) for clicking a file Claude names.
