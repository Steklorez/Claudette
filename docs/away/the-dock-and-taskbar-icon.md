---
title: The Dock and taskbar icon
parent: Away from your desk
nav_order: 2
summary: "The icon in the Dock or on the taskbar shows how many tabs need you and whether Claude is working, without bringing the window up."
---

# The Dock and taskbar icon

Claudette's icon in the Dock (macOS) or on the taskbar (Windows) tells you what your tabs are doing without bringing the window up: how many tabs need you, whether any are working, and whether a usage limit has stopped a task. Linux has no badge or animation. There, [notifications](notifications.md) are all you get.

## The badge

The icon shows the number of tabs that need your input: a waiting permission prompt, question or plan.

- On Windows it's an overlay on the taskbar button.
- On macOS it's the Dock icon's badge.

Turn it off with Settings → Notifications → **Show the number of tabs needing input on the Dock or taskbar icon** (on by default).

## The animation

While tabs work or wait, the icon moves. Settings → Notifications → **Animate the Dock or taskbar icon while tabs are working or waiting** (on by default) turns it off.

What it shows, most important first:

| State | Windows (the overlay on the taskbar button) | macOS (the whole Dock icon) |
|---|---|---|
| A tab needs your input | The number of tabs | Claudette waves, under the badge's number |
| A usage limit has stopped a task and hasn't reset | An hourglass, its sand running slowly down before it turns over | Claudette stands by the hourglass |
| A tab is working | Claude's spark, pulsing | Claudette types |

The hourglass shows whether or not the task will continue by itself, and goes once the limit resets, a turn starts, or you choose **Don't continue** ([When a limit stops a task](../usage/when-a-limit-stops-a-task.md)). It comes before tabs working, since the limit stopped a task and the other tabs are likely to run into it too. It never flashes the taskbar, because nothing needs you.

On Windows the overlay holds one image, so the number of tabs needing input comes first, then the hourglass, then the spark. The spark or hourglass comes back once no tab needs input, or at once with the badge off. Windows takes the button's own icon from the app, so only the overlay can move.

With reduced motion, the icon shows its picture without moving. That's Settings → Appearance → **Motion**; see [Accessibility](../settings/accessibility.md).

## Flashing

On Windows, when a tab starts needing input while Claudette isn't in front, the taskbar button flashes until you bring Claudette to the front or no tab needs input any more. It follows **A tab needs permission or an answer** in Settings → Notifications: turn that off and the button doesn't flash. The Dock has no flash. The waving does that job.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Show the number of tabs needing input on the Dock or taskbar icon** | On | Settings → Notifications |
| **Animate the Dock or taskbar icon while tabs are working or waiting** | On | Settings → Notifications |
| **A tab needs permission or an answer** (flashing) | On | Settings → Notifications |

## See also

- [Notifications](notifications.md) for what the OS shows as a message.
- [The sidebar](../tabs/the-sidebar.md) for the icons on each tab's row.
