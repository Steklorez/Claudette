---
title: Keeping the computer awake
parent: Away from your desk
nav_order: 5
summary: "Long tasks outlast the time before a computer goes to sleep."
---

# Keeping the computer awake

Long tasks outlast the time before a computer goes to sleep. **Keep this computer awake** holds off sleep, so a turn isn't cut off, and so a task that a [usage limit](../usage/when-a-limit-stops-a-task.md) stopped can carry on by itself when the limit resets, hours later.

Only sleep is held off. The screen can still turn off and the screensaver can still start. Closing a laptop's lid may still put it to sleep. It's a choice for this machine and doesn't sync.

## Turning it on

Either:

- Turn on Settings → General → **Keep this computer awake** (off by default).
- Click the cup in the usage header. A click turns it on or off.

Then choose when it applies:

- **Only while Claude has work to finish** (the default once it's on). That means a tab is running a turn (not one waiting for you, since you may not be there to answer), Claude Code is keeping tasks or agents running in the background in any tab, or a task is waiting for a usage limit to reset so it can carry on by itself. A task that won't continue, because you chose **Don't continue** or the reset is more than a day away, doesn't count. When there's no work left, Deventry holds on for two more minutes, since the next turn often starts right after, and then lets the computer sleep as your OS settings say.
- **Whenever Deventry is running**, from when it starts until it closes.

Right-click the cup to choose between them, under **Keep awake** in its menu. Choosing turns the setting on too. The same menu's **Keep-awake settings…** opens Settings → General. The menu's first section is your [timed messages](timed-messages.md), and under **Keep awake** is [When Claude is done](#when-claude-is-done).

## The cup

The cup sits in the usage header, after the processes' total. It takes the accent color while the setting is on, and steams while sleep is being held off, for any reason. Its tooltip says what's happening and why, for example *Keeping this computer awake. The screen can still turn off.* and *While Claude has work to finish: 2 tabs working, 1 tab waiting for a usage limit to reset.*

While any [timed message](timed-messages.md) is on, the cup has a clock's two hands in its body. Sleep is held off from just before a timed message's time until its tab's turn has finished, whatever the setting says.

Tabs connected to the [Claude app](the-claude-app.md) also keep the computer awake while Settings → General → **Also while tabs are connected to the Claude app** is on, and so does a bot of [Remote clients](remote-clients.md) while it's on, as long as **Also while a remote client is on** is. The cup steams for those too, and the tooltip names them.

## When Claude is done

{% include unreleased.html %}

Leaving a long task to finish overnight? Right-click the cup and, under **When Claude is done**, choose **Shut down** or **Sleep**: once Claude has nothing left to do, Deventry shuts the computer down or puts it to sleep. Choosing one clears the other, and choosing it again turns it off.

- **Done** means no tab is running a turn, keeping tasks running in the background, waiting for a usage limit to reset or waiting for you to answer a prompt; no [project tool](../project-tools/using-project-tools.md)'s job, such as a build, is running; and no [timed message](timed-messages.md) is due within the hour. A computer that's off can't be woken for one, so a shutdown waits for its turn first. A sleeping computer can, so **Sleep** waits only for one that's about to go, unless Deventry can't wake this computer.
- **Until then**, a bar under the header says what it's waiting for, such as *Shuts down once Claude is done: waiting for 2 tabs working.*, with **Cancel**. The computer is kept awake meanwhile, whatever **Keep this computer awake** says, so it gets there. The cup's tooltip says so too.
- **A minute's warning.** Once Claude is done, the bar counts down a minute, *Shutting down in 42 seconds.*, with **Shut down now** (or **Sleep now**) and **Cancel**, and a notification says so if Deventry isn't in front. Your [remote clients](remote-clients.md) are told too, with a sound if you're away, and their **Cancel** cancels it from your phone, whatever they're allowed to do. If Claude starts working again in that minute, the countdown stops, and starts again once it's done.
- **Shut down** closes Deventry first, as its window's close button does, keeping your tabs for next time, then shuts the computer down. It isn't forced, so an app with unsaved work can still stop it. **Sleep** leaves Deventry as it is, there when the computer wakes.
- **Once only.** It turns itself off once it has happened or you cancel it, and Deventry starts without it.

On Windows, a computer with Modern Standby, which sleeps when its screen goes off, is put to sleep by turning its screen off. On macOS, the first time you choose **Shut down**, macOS asks whether Deventry may control System Events, which is how it shuts a Mac down; if you don't allow it, the bar says so, and you can allow it later in System Settings → Privacy & Security → Automation. On Linux, if shutting down or sleeping would ask for a password, which nobody may be there to type, the bar says so when you choose it.

## When it can't

Deventry holds sleep off with the OS's own call on Windows, with `caffeinate` on macOS and with `systemd-inhibit` on Linux. Without `systemd-inhibit`, Linux can't be kept awake: the cup is disabled and so is the setting, and they say why. See [Something missing on Linux](../troubleshooting/something-missing-on-linux.md).

Settings → Advanced → Diagnostics shows whether the setting is on, what Deventry is waiting on, and whether the computer is being kept awake.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Keep this computer awake** | Off | Settings → General, or the cup |
| **Only while Claude has work to finish** / **Whenever Deventry is running** | **Only while Claude has work to finish** | Settings → General, or the cup's menu |
| **Also while tabs are connected to the Claude app** | On | Settings → General |
| **Also while a remote client is on** | On | Settings → General |

## See also

- [The usage header](../usage/the-usage-header.md), where the cup lives.
- [The Claude app](the-claude-app.md).
- [Remote clients](remote-clients.md).
- [Timed messages](timed-messages.md), which the cup shows too.
