---
title: A folder's account
parent: Accounts
nav_order: 3
---

# A folder's account

{% include unreleased.html %}

Each folder group in the sidebar has an account that its new tabs run under. Pick it once for a folder, and every tab you open there afterward uses it.

{% include shot.html name="accounts-standard" alt="A tab on the Studio API key: the header shows the key's estimated spend, and Studio's tag sits on the orbit-game group and on a tab that was moved to it" %}

## Choosing it

1. Right-click the group's label in the sidebar (or its color in the narrow rail).
2. Choose **Account ▸**. It lists **Default** and each of your accounts with its color, with the group's current one ticked.
3. Click an account.

Tabs already open in the group keep theirs. Each has its own **Account ▸**: see [Moving a tab to another account](moving-a-tab-to-another-account.md). The choice is remembered for the folder on this machine, however its path was written.

The menu only shows once you have an account besides Default.

## New folders

A folder you open for the first time uses Settings → Accounts → **New folders use**, which is Default unless you change it.

## The tags

Each account has a color: the first group color no other account has, which you can change in Settings → Accounts with **Tag color**.

- A group whose new tabs use an account other than Default has a small tag with the account's name beside its label. Hover over it for *New tabs here run under Studio*.
- A tab whose account isn't its group's has its account's tag on its row. Default's tag is muted. Hover over it for which account it is.
- Once you have an account besides Default, the tab's info card has an **Account** row, such as *Studio (API key)* or *Default*.

## Settings

| Setting | Default | Where |
|---|---|---|
| **New folders use** | Default | Settings → Accounts |
| **Tag color** | The first free group color | Settings → Accounts, on each account |

## See also

- [API-key accounts](api-key-accounts.md) for adding an account.
- [Folder groups](../tabs/folder-groups.md) for the rest of a group's menu.
- [The tab info card](../tabs/the-tab-info-card.md).
