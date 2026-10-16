---
title: Reporting an issue
parent: Troubleshooting
nav_order: 9
---

# Reporting an issue

Problems with Claudette go to its [GitHub issues](https://github.com/dotBunny/Claudette/issues). The Settings window has what you need to report one, with the versions already gathered.

## The version

The foot of the Settings sidebar shows which Claudette this is, on every page: *Claudette 0.3.0*. A copy run from source adds the commit it was built from, such as *Claudette 0.3.0 · 842169b*. See [Running from source](../getting-started/running-from-source.md).

Click it to copy the versions for a bug report, one per line. It says *Copied* for two seconds. The lines are:

- Claudette's version and how it was installed: an MSIX, a `.dmg`, or a source build and its commit.
- Claude Code's version.
- The operating system and its identifier.
- The .NET runtime.

Only versions: never paths, names or account details, because it's meant to be posted.

## Report an issue

**Report an issue**, at the foot of the Settings sidebar, opens a new issue on Claudette's GitHub page in your browser. It starts with an outline, *what happened*, *what you expected* and *steps to reproduce*, and the same versions filled in. Nothing is sent until you submit it on GitHub.

1. Choose **Report an issue** in Settings.
2. Fill in the outline. Say what you were doing, what you expected and what happened instead.
3. Add what helps: the text of an error the tab showed, the tab's status, and whether it happens every time.
4. Review everything, then submit.

## What helps

- **Which part.** The page of this guide that describes it, or the name of the button.
- **Diagnostics.** **Copy diagnostics** in Settings → Advanced adds the login shell line, the Claude app line and counts of messages Claudette didn't know. Read it first: it includes the path to `claude`. See [Diagnostics and logs](diagnostics-and-logs.md).
- **A protocol log** when Claude Code's behavior is the question. It holds prompts and file contents, so cut it down to the lines that matter, or describe them.
- **Whether it happens in a terminal.** If plain `claude` does the same thing, the problem is probably Claude Code's, not Claudette's, and belongs with Anthropic.

Leave out anything private: your prompts, project paths and account details.

## See also

- [Diagnostics and logs](diagnostics-and-logs.md)
- [Claude is having problems](claude-is-having-problems.md), when Claude itself is down.
