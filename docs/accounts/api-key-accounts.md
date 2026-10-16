---
title: API-key accounts
parent: Accounts
nav_order: 2
summary: "An API-key account lets a tab run Claude Code on an API key instead of your Claude sign-in."
---

# API-key accounts

An API-key account lets a tab run Claude Code on an API key instead of your Claude sign-in. It suits a work key beside a personal plan. API keys are the only kind of account you can add besides Default.

{% include shot.html name="guide/accounts/settings-accounts" alt="Settings → Accounts with an account named Studio: its name and tag color, its two spend warnings, Change key and Remove, and the form for adding another key" %}

## Adding an account

1. Open Settings → Accounts.
2. Under **Add an API key**, type a **Name**, such as Work, and paste the **API key**. The key is hidden as you type.
3. Click **Add account**.

The account appears above the form. Now choose where it's used: [a folder's account](a-folders-account.md) for a folder's new tabs, or [moving a tab](moving-a-tab-to-another-account.md) for one that's open.

## Where the key is kept

The key goes only to your computer's credential store: Windows Credential Manager, the macOS Keychain, or the Secret Service through `secret-tool` on Linux. It's never in Claudette's files, a command line, the environment or a log, and it's never synced. When a tab starts, Claude Code asks Claudette for the key, so it doesn't have to be anywhere else.

On Linux, adding an account is off until `secret-tool` is installed (the `libsecret-tools` package), and Settings says why.

## What an account's tabs share

A tab on an API-key account runs in the same Claude Code configuration folder as the Default account's, so it shares everything else with it: settings, MCP servers, plugins, skills, transcripts and [History](../history/history.md). Moving a tab between accounts copies nothing.

The account's tabs start without `ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN`, so the key you added is the one used. Default's tabs keep them if you've set them.

## Changing and removing an account

Each account in Settings → Accounts has:

- A name box. Edit it and click away to rename it.
- **Tag color**, the color of its tag: see [A folder's account](a-folders-account.md#the-tags).
- **Warn when today's estimated spend passes ($)** and **Warn when this month's estimated spend passes ($)**: see [Spend and its warnings](spend-and-its-warnings.md).
- **Change key…**, which shows a box for the new key. Click **Save key**, or **Cancel**. Tabs pick up the new key within a few minutes, or when they start again.
- **Remove…**, which asks first, in place. It's off while an open tab runs under the account: move those tabs to another account, or close them. Removing it deletes its key, and folders that used it use Default again.

## Settings

| Setting | Default | Where |
|---|---|---|
| **New folders use** | Default | Settings → Accounts |

**New folders use** is the account a folder uses the first time you open it. The accounts are yours, so **Reset to defaults** on this page only sets **New folders use** back to Default and leaves them alone. Each machine sets up its own accounts: they don't sync, and neither do keys.

## If Claude Code refuses the key

A key Claude Code won't use, or can't get, is that tab's own error: *Claude Code couldn't use Work's API key. Check the key in Settings → Accounts, or move the tab to another account.* If Claudette is installed in a folder whose path has a quote, `$`, `%` or a backtick in it, a tab can't use an API-key account, and says why.

## What an API key doesn't have

- **Plan limits.** There are no meters for it: [Spend and its warnings](spend-and-its-warnings.md) covers what the header shows.
- **The Claude app.** Its tabs can't connect to it, since that needs a Claude sign-in. **Connect to the Claude app** is off, with the reason as its tip. See [The Claude app](../away/the-claude-app.md).

## See also

- [Your Claude sign-in](your-claude-sign-in.md) for the Default account.
- [Passwords and keys](../privacy/passwords-and-keys.md) for how keys are stored.
