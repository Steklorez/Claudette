---
title: Privacy policy
nav_exclude: true
description: Claudette's privacy policy. Claudette collects nothing about you, and this site doesn't either.
# Its address before the site's were folders, which the Microsoft Store listing names (GUIDE.md, "Privacy policy").
redirect_from: /privacy-policy.html
---

# Privacy policy

Last updated · October 23, 2026

This policy covers Claudette, the desktop app for Claude Code, however you got it: from the Microsoft Store, from its [Downloads page](downloads.md), or built from its source. It also covers this website. We've kept it short and in plain English.

**The short version: we don't collect anything.** Claudette has no account, no analytics, no telemetry, no crash reporting and no ads. It doesn't send us anything, and there's no server of ours for it to send to.

## 01 What we collect

**Nothing.** What Claudette knows, such as your settings, saved tabs, drafts, usage history and logs, stays in its data folder on your computer. You can see what's there in [Where Claudette keeps things](privacy/where-claudette-keeps-things.md).

**Passwords and keys** you give Claudette, such as a Perforce password or an API key, are kept in your operating system's own credential store. They go only to the program on your computer that needs them, and are never synced, logged or sent to us. See [Passwords and keys](privacy/passwords-and-keys.md).

## 02 What leaves your computer

Claudette itself reaches a few services, none of them ours, and sends them nothing about you:

- **GitHub**, to check for a new version of Claudette and download it. The check is a plain request, without signing in. A copy from the Microsoft Store doesn't check: the Store updates it. You can turn the check off in Settings → General.
- **Anthropic's status page** (status.claude.com), to show when Claude is having problems. You can turn it off in Settings → General.
- **An image's own address**, only when you choose **Load image** on a web image in a reply.

Some things leave your computer only if you set them up:

- **The session library.** If you put it in a folder that a service such as OneDrive, Google Drive, Dropbox or iCloud Drive keeps in sync, that service uploads what's in it. Claudette asks you to confirm before it uses such a folder.
- **The Claude app.** A tab you connect to the Claude app has its conversation stored by Anthropic. It's off until you turn it on, tab by tab.

Claudette also starts other programs for you, such as `git`, `p4` and diff tools. They talk to whatever they'd talk to from your terminal, such as your git remote or your Perforce server. [What leaves this computer](privacy/what-leaves-this-computer.md) lists every case.

## 03 Claude Code and Anthropic

Claudette runs Claude Code, which you install and sign in to yourself. Claude Code sends your prompts, and the files it reads, to Anthropic's models, as it does in a terminal. That's between you and Anthropic, under Anthropic's [privacy policy](https://www.anthropic.com/legal/privacy) and Claude Code's [data usage](https://code.claude.com/docs/en/data-usage) page. Claudette doesn't see that traffic, add to it or change it.

Claudette is made by dotBunny. It isn't made by, or affiliated with, Anthropic.

## 04 The Microsoft Store and GitHub

- **The Microsoft Store** installs and updates Claudette under Microsoft's [privacy statement](https://privacy.microsoft.com/privacystatement). We don't see who installs it.
- **GitHub** hosts Claudette's downloads, source and issues under GitHub's [privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). An issue you open there is public.
- **Reporting a problem.** **Report an issue** in Claudette's Settings opens a new GitHub issue in your browser, with Claudette's, Claude Code's, the operating system's and .NET's versions filled in: only versions, never paths, names or account details. Nothing is sent until you submit it on GitHub. See [Reporting an issue](troubleshooting/reporting-an-issue.md).

## 05 This website

This site has no analytics, no cookies and no trackers, and loads nothing from other sites. It's hosted on GitHub Pages, so GitHub may keep the usual server logs, such as your IP address, under its privacy statement.

Your browser remembers one thing for this site: whether you widened the guide's reading width. That stays in your browser and is never sent anywhere.

## 06 Children

Claudette and this site collect nothing from anyone, children included.

## 07 Changes

If what Claudette sends ever changes, we'll update this page and the date at the top before that version is released, and say so in the [changelog](https://github.com/dotBunny/Claudette/blob/main/CHANGELOG.md).

## 08 Contact

Claudette is published by dotBunny Inc., in Peterborough, Ontario, Canada. Questions about this policy, or about your privacy, go to [legal@dotbunny.com](mailto:legal@dotbunny.com).
