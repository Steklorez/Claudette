---
title: Something missing on Linux
parent: Troubleshooting
nav_order: 6
summary: "Claudette builds and runs on Linux, but only Windows and macOS are tested and packaged, so it's less polished there."
---

# Something missing on Linux

Claudette builds and runs on Linux, but only Windows and macOS are tested and packaged, so it's less polished there. A few features rely on programs a Linux desktop may not have. Without them the feature is off, and Claudette says why.

## Notifications: `notify-send`

Claudette shows notifications through `notify-send`, which also reports a click on one. Without it there are no notifications. Settings → Notifications says *Notifications aren't available here* and mentions `notify-send`. Install it, usually with your distribution's libnotify tools package, and restart Claudette.

## Keeping the computer awake: `systemd-inhibit`

**Keep this computer awake** and the header's cup hold off sleep with `systemd-inhibit`. Without it, the cup is disabled and says *systemd-inhibit wasn't found, so Claudette can't keep this computer awake.* The setting is disabled too, and so is keeping it awake for tabs connected to the Claude app. Settings → Advanced → **Diagnostics** says whether the computer is being kept awake, or why not. See [Keeping the computer awake](../away/keeping-the-computer-awake.md).

## Passwords: `secret-tool`

Claudette keeps a Perforce password in the desktop's Secret Service, through `secret-tool`. Without it, Settings says *Install secret-tool (the libsecret-tools package) to store passwords in the Secret Service*, and Perforce passwords can only be asked for each time. See [Perforce](../project-tools/perforce.md).

Install the `libsecret-tools` package, then restart Claudette.

## API keys for accounts: `secret-tool`

{% include unreleased.html %}

An API-key account's key goes in the same store, through `secret-tool`. Without it, adding an API key in Settings → Accounts is off, and Settings says why. Install `libsecret-tools` as above.

## Environment

Programs missing from Claude's `PATH` are a different problem: see [Claude can't find a program](claude-cant-find-a-program.md). Claudette started from a launcher reads your login shell's environment, as on macOS.

## A `dotnet run` build on macOS

This one isn't Linux, but it looks the same. macOS notifications need Claudette's app bundle, so a build started with `dotnet run` has none, and Settings → Notifications says *On macOS, Claudette has to run from its app (not with dotnet run)*. Run the installed app to get them.

## See also

- [Windows, macOS and Linux](../reference/windows-macos-and-linux.md)
- [Running from source](../getting-started/running-from-source.md)
