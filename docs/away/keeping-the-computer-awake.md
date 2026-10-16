---
title: Keeping the computer awake
parent: Away from your desk
nav_order: 4
summary: "Long tasks outlast the time before a computer goes to sleep."
---

# Keeping the computer awake

Long tasks outlast the time before a computer goes to sleep. **Keep this computer awake** holds off sleep, so a turn isn't cut off, and so a task that a [usage limit](../usage/when-a-limit-stops-a-task.md) stopped can carry on by itself when the limit resets, hours later.

Only sleep is held off. The screen can still turn off and the screensaver can still start. Closing a laptop's lid may still put it to sleep. It's a choice for this machine and doesn't sync.

## Turning it on

Either:

- Check Settings → General → **Keep this computer awake** (off by default).
- Click the cup in the usage header. A click turns it on or off.

Then choose when it applies:

- **Only while Claude has work to finish** (the default once it's on). That means a tab is running a turn (not one waiting for you, since you may not be there to answer), Claude Code is keeping tasks or agents running in the background in any tab, or a task is waiting for a usage limit to reset so it can carry on by itself. A task that won't continue, because you chose **Don't continue** or the reset is more than a day away, doesn't count. When there's no work left, Claudette holds on for two more minutes, since the next turn often starts right after, and then lets the computer sleep as your OS settings say.
- **Whenever Claudette is running**, from when it starts until it closes.

Right-click the cup to choose between them. Choosing turns the setting on too.

## The cup

The cup sits in the usage header, after the processes' total. It takes the accent color while the setting is on, and steams while sleep is being held off, for any reason. Its tooltip says what's happening and why, for example *Keeping this computer awake. The screen can still turn off.* and *While Claude has work to finish: 2 tabs working, 1 waiting for a usage limit to reset.*

Tabs connected to the [Claude app](the-claude-app.md) also keep the computer awake while Settings → Claude Code → **Keep this computer awake while tabs are connected** is on, and the cup steams for that too. The tooltip names it.

## When it can't

Claudette holds sleep off with the OS's own call on Windows, with `caffeinate` on macOS and with `systemd-inhibit` on Linux. Without `systemd-inhibit`, Linux can't be kept awake: the cup is disabled and so is the setting, and they say why. See [Something missing on Linux](../troubleshooting/something-missing-on-linux.md).

Settings → Advanced → Diagnostics shows whether the setting is on, what Claudette is waiting on, and whether the computer is being kept awake.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Keep this computer awake** | Off | Settings → General, or the cup |
| **Only while Claude has work to finish** / **Whenever Claudette is running** | **Only while Claude has work to finish** | Settings → General, or the cup's menu |
| **Keep this computer awake while tabs are connected** | On | Settings → Claude Code |

## See also

- [The usage header](../usage/the-usage-header.md), where the cup lives.
- [The Claude app](the-claude-app.md).
