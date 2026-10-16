---
title: Signing in
parent: Troubleshooting
nav_order: 2
---

# Signing in

[Signing in](../getting-started/signing-in.md) describes the sign-in screen. This page is for when it doesn't work. A sign-in that fails shows Claude Code's own message and a **Try again** button, which repeats the same kind of sign-in.

## The browser didn't open, or opened the wrong one

Choose **Open browser again**. It reopens the same address. If your default browser or profile isn't the one with your Claude account, copy the address into the right one: choose **Enter a code instead**, which opens the page that shows a code, and paste the code back.

## The browser finished but Claudette didn't notice

The automatic sign-in comes back to a port on your own computer. A firewall, a browser on another device, or a remote session can stop that.

1. Choose **Enter a code instead**.
2. Sign in on the page that opens. It shows a code.
3. Copy the whole code, including what follows the `#`, into the field, and choose **Submit code**.

If you paste only part of it, Claude Code may answer *Invalid code. Please make sure the full code was copied.* Claudette keeps waiting, so paste the whole code again.

## It timed out, or Claude Code stopped

- *Sign-in timed out. Try again.* A sign-in that waits too long is given up. Choose **Try again**.
- *Claude Code stopped while signing in.* Choose **Try again**. If it keeps stopping, check Claude Code in a terminal with `claude auth status`.

## An organization that isn't allowed

Claude Code refuses an account from an organization your own organization's settings don't allow, with a message that says so. Sign in with an account from an allowed organization: **Try again** and sign in with the other account in your browser. Your organization's settings can also limit which kind of account may sign in, or require its own sign-in. A tab then says so when it can't start: see [A tab that won't start](a-tab-that-wont-start.md).

## Another kind of account

Use **More options** for **Sign in with an Anthropic Console account (API usage billing)** or **Sign in with SSO**. SSO signs in through `claude auth login`.

## I signed in but Claudette still says I'm not

- If you signed in from a terminal, choose **I've already signed in**. Claudette asks Claude Code again.
- If it still says *Claude Code still reports that you're not signed in.*, Claude Code is looking somewhere else. Check that your terminal and Claudette use the same configuration folder: `CLAUDE_CONFIG_DIR` changes where Claude Code keeps its sign-in.
- An API key or token in your environment, such as `ANTHROPIC_API_KEY`, can make Claude Code report a sign-in that signing out doesn't remove. Claudette says so after you sign out.

## Claude Code is signed out in my terminal too

That's by design. There is one sign-in, Claude Code's, and **Sign out…** in Claudette signs Claude Code out everywhere on the computer, so Claudette asks first. Sign in again from either place.

## Messages sent while signed out

They aren't lost. They're held, with their images, and sent in order once you're signed in.

## See also

- [Your Claude sign-in](../accounts/your-claude-sign-in.md)
- [Diagnostics and logs](diagnostics-and-logs.md)
