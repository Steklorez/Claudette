---
title: Claude is having problems
parent: Troubleshooting
nav_order: 7
summary: "When Claude itself has trouble, tabs fail in ways that look like Claudette's fault or yours: retries, overloaded errors, a sign-in that won't finish."
---

# Claude is having problems

When Claude itself has trouble, tabs fail in ways that look like Claudette's fault or yours: retries, overloaded errors, a sign-in that won't finish. Claudette reads Claude's public status page and tells you, with a dot in the header and a banner across the top while there's an incident.

## The dot

The dot sits at the right of the header, before your account's name.

| Color | Level |
|---|---|
| Green | *Claude is operational* |
| Amber | *Claude is having problems* (degraded), or *Claude is under maintenance* |
| Red | *Claude has an outage* |
| Grey | *Status unknown*, or not checked yet |

Hover it for the level, each service Claudette watches and its status, the latest incident and where it's at, any maintenance under way, and when it last checked. Click it to open status.claude.com.

Claudette watches the services it relies on: Claude Code, the Claude API (what Claude Code calls for the model) and claude.ai (sign-in, and the Claude app). The Console and other products don't count.

## The banner

While a watched service isn't operational, or an open incident concerns one, a banner runs across the top under the header. It names the latest incident and where it's at: *Claude is having problems: Elevated errors on claude.ai, Claude Code and the Claude API (investigating)*, with *and 1 more incident* when there are others. Without an incident, it names the services that aren't well. Maintenance alone gets a quieter banner, *Claude maintenance in progress:* with its name.

- **Status page** opens the incident's page on status.claude.com.
- **Dismiss** hides the banner until a different incident or maintenance arrives, or things get worse. It's remembered on this computer.

The banner goes by itself when everything is operational again, and the dot stays, green. If a check fails, the banner hides and the dot goes grey, because the status is then unknown.

## When it checks

- At launch, before Claude Code is found or signed in, since an incident can be why those fail.
- Every 5 minutes after that. A failed check backs off to 10 minutes, then 20, then 30, and the first good answer goes back to every 5.
- Straight away, at most once a minute, when a tab's request fails on Anthropic's side: a server error or an overloaded reply that Claude Code retries. A rate limit, a sign-in problem or a bad request is yours, and doesn't trigger a check.

The check reads status.claude.com's public summary. There's no sign-in, and Claudette sends nothing but the request.

## What to do

- **Wait.** If the banner names an incident that matches what a tab shows, it's Claude's. Tabs retry by themselves, and a retry note in the conversation updates as they do.
- **Check the page.** **Status page** shows what Anthropic says about it.
- **If everything is green and a tab still fails,** the cause is elsewhere: see [A tab that won't start](a-tab-that-wont-start.md), [Signing in](signing-in.md) and [A tab that seems stuck](a-tab-that-seems-stuck.md).

## Settings

Settings → General → **Show Claude's service status**, on by default. Off hides the dot and the banner and stops the checks. It's this computer's and doesn't sync.

## See also

- [A tour of the window](../getting-started/a-tour-of-the-window.md), for where the dot is.
- [What leaves this computer](../privacy/what-leaves-this-computer.md)
