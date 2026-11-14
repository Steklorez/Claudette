---
title: Timed messages
parent: Away from your desk
nav_order: 6
summary: "A message Deventry sends at a time you choose, to a new tab in a project, even if the computer is asleep."
---

# Timed messages

A timed message is one you write ahead, which Deventry sends at a time you choose to a new tab in a project: a morning triage at 7:00 every weekday, a check on last night's build, a reminder for Claude once on a date. When the time comes, Deventry opens the tab, sends the message as if you'd typed it, and Claude gets to work. If the computer is asleep then, Deventry wakes it to send the message.

A message is sent only while Deventry is running. One whose time comes while Deventry is closed isn't sent, then or later.

## Adding one

Any of these opens Settings → **Timed messages** on a new message:

- **Add a timed message…** in a project group's menu, with the group's folder filled in.
- **Add a timed message…** in the cup's menu (right-click the cup in the usage header).
- **Add a timed message…** in the command palette.
- **Add a timed message…** on the Settings page itself.

{% include shot.html name="guide/away/timed-messages" alt="Settings on Timed messages, with the editor of a new message: Name, Morning triage; Project, the starfall folder, with Browse…; Message, Triage the issues filed overnight; Opens in, A new tab or A new tab in a worktree; Mode, Model and Effort, each Project default; and When, with Once, On days and Advanced, a time, the days of the week from Mon to Sun with Mon to Fri on, Every day and Weekdays, From and Until, and the next three times it will be sent. Add and Cancel are at the bottom." %}

Then fill in:

- **Name**, such as *Morning triage*. The cup's menu, the notifications and the message's card use it.
- **Project**: the folder the tab opens in, from your open groups and recent folders, or **Browse…**.
- **Message**: what Claude is asked, as you'd type it in the message box.
- **Opens in**: **A new tab** in the folder, or **A new tab in a worktree** of its own, named after the message, so its changes don't meet your main checkout's. A folder outside a git repository always gets a plain tab.
- **Mode**, **Model** and **Effort**: **Project default**, or one chosen for this message. Nobody may be there to answer a permission prompt, so think about the mode: **Plan** changes nothing without you, and **Bypass permissions** lets Claude run anything. A [remote client](remote-clients.md) can answer prompts from your phone.
- **When** (below).

The editor shows the next three times the message will be sent, or what's missing before it can be added.

## When

- **Once**: a date and a time.
- **On days**: a time, and the days of the week. **Every day** and **Weekdays** set them at once.
- **Advanced**: a cron expression, with five fields: minute, hour, day of the month, month and day of the week. `0 7 * * MON-FRI` is 7:00 on weekdays; `30 9,14 1 * *` is 9:30 and 14:30 on the 1st of each month. Lists, ranges, steps (`*/15`) and the days' and months' names work, and Sunday is 0 or 7. If you give both a day of the month and a day of the week, either one matching is enough, as cron has it. One that doesn't read says where and why.
- **From** and **Until**, for **On days** and **Advanced**: no message before or after those dates.

Times are your computer's own. A time the clock skips when summer time starts is sent when the clock jumps past it, and a time that happens twice when it ends is sent once.

## When the time comes

Deventry opens the message's tab without taking you away from the tab you're in, and sends the message. Its card says *Timed message: Morning triage* over it. A notification says *Timed message sent: Morning triage in starfall*, and from then on it's a tab like any other: its row shows Claude working, and you're told when it needs you or finishes, here and on your [remote clients](remote-clients.md).

If Claude Code asks whether to trust the folder first, the tab waits for you, and the notification says so.

### If the computer is asleep

Deventry wakes the computer a minute before the time, so the network is back by then, and keeps it awake until the tab's turn has finished.

- **Windows** lets Deventry wake the computer if your power plan allows it: **Allow wake timers**, in Control Panel → Power Options → Change plan settings → Change advanced power settings → Sleep. Windows' own plans allow it when plugged in, but not on battery. Settings → Timed messages tells you when your plan won't wake for Deventry.
- **macOS and Linux** need an administrator to set a wake, so Settings → Timed messages has **Install the wake helper…**, which asks for an administrator's password once. It installs a small script that can only set or clear one wake within the next month, and a rule that lets you run that script, and nothing else, without a password: a sudoers rule on macOS, a polkit rule on Linux, which also needs `rtcwake`. **Remove the wake helper…** takes it away again.

If the computer couldn't be woken, a message noticed when it wakes is still sent if it's under half an hour late. Later than that it's skipped, with a notification, and the cup's menu says so until it's next sent.

## The cup

While any timed message is on, the [cup](keeping-the-computer-awake.md#the-cup) in the usage header has a clock's two hands in its body, and its tooltip ends with the next one: *Next timed message: Morning triage in starfall, tomorrow at 7:00 AM.*

{% include shot.html name="guide/away/timed-messages-cup" alt="The cup's menu, in two sections. Timed messages: Morning triage · starfall · Mon at 7:00 AM, then Add a timed message…. Keep awake: Only while Claude has work to finish, Whenever Deventry is running, Keep-awake settings… and Timed messages settings…." %}

Right-click it for the **Timed messages** section of its menu: each message, with when it's next sent, opening **Edit…** and **Clear…**. **Clear…** asks first, then deletes the message. With more than one, **Clear all…** deletes them all.

## Changing and deleting them

Settings → **Timed messages** lists them all, each with what it is, where, when and its next time. Its switch turns it off without deleting it. **Edit…** opens it in the editor, and **Delete…** asks, then deletes it.

Timed messages belong to this computer and don't sync to your others, so two machines never start the same work.

## Settings

| Setting | Default | Where |
|---|---|---|
| The messages | None | Settings → Timed messages, or the cup's menu |
| **A timed message is sent or skipped** | On | Settings → Notifications |

## See also

- [Keeping the computer awake](keeping-the-computer-awake.md), whose cup they share.
- [Remote clients](remote-clients.md), to answer a timed tab's prompts from your phone.
- [Notifications](notifications.md).
