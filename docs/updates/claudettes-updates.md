---
title: Claudette's updates
parent: Updates
nav_order: 2
summary: "An installed Claudette checks for a newer version, downloads it, installs it and restarts into it, with every tab as it was."
---

# Claudette's updates

An installed Claudette checks for a newer version, downloads it, installs it and restarts into it, with every tab as it was. What's new in each version is in the [changelog](https://github.com/dotBunny/Claudette/blob/main/CHANGELOG.md).

## How it checks

At launch and every 6 hours, Claudette asks GitHub for Claudette's releases. It doesn't sign in, and the request carries nothing about you. It offers the newest release above the version you're running, once that release is published.

- Pre-releases count only with **Include pre-releases** on.
- A copy installed from the Microsoft Store doesn't check either: the Store keeps it up to date, and Settings → General says so.
- A source build doesn't check for releases. It checks the branch its checkout tracks for new commits, pulls and builds them, and restarts into each new build: see [Running from source](../getting-started/running-from-source.md#when-there-are-new-commits).

## The badge

A badge at the foot of the sidebar says *Claudette 1.3.0 is available*. It changes as things happen: *Downloading Claudette 1.3.0… 40%*, *Restart to update to 1.3.0* once the package is downloaded, and *Updating when idle…* while it waits. Choose it to open a card with the release's notes, when it was released, and these buttons:

- **Download and restart** downloads the package and restarts into the new version. It becomes **Restart to update** once the package is downloaded.
- **Update when idle**, while a tab is working: Claudette waits until no tab is starting, working or waiting on you, and then updates. **Don't update** cancels the wait.
- **Download** fetches the package now, to install later. **Cancel download** stops it.
- **Skip this version** hides the badge until a newer release comes out. It's remembered on this machine.
- **Release notes on GitHub** opens the release's page.
- **Open the installer** (Windows) or **Open the disk image** (macOS) appears when Claudette can't install the package itself: see [When an update fails](when-an-update-fails.md).

Settings → General has the same actions under **Updates and status**.

## What an update does

1. Claudette saves a snapshot of every tab, each unsent message, the selected tab and the window's size and place.
2. It stops its tabs as closing does, and installs the new version.
3. The new version opens, takes the tabs from the snapshot and carries on.

A tab that was working is carried on: Claude Code re-runs the turn by itself, once, and the conversation says *Carrying on with the turn the restart cut off.* Processes the tabs started are stopped, as when Claudette closes. The card warns you when a tab is working, and **Update when idle** is the way to avoid interrupting it.

- **On Windows**, Windows closes Claudette, installs the MSIX and starts the new version.
- **On macOS**, a small helper swaps `Claudette.app` once Claudette has quit. It renames the old app to `Claudette.app.previous` first, so it can put it back if the new version doesn't start.

## Which package it uses

Claudette picks the package made for this install: the MSIX for an MSIX install on Windows, and the `.dmg` for `Claudette.app` on macOS, for your processor. A copy that can't replace itself, such as Linux or a Windows copy that isn't the package, still hears about the release, with a link to its page and the message *This copy of Claudette can't update itself. Download the new version from its release page.*

Before it installs anything, Claudette checks the download against its size and the SHA-256 digest GitHub publishes for it, and deletes one that doesn't match. A package GitHub publishes no digest for isn't downloaded at all: the card says *GitHub published no checksum for this release's package*, and links to the release page. It then checks the package itself, for the right identity, version and processor, and on macOS a valid signature. If anything fails, nothing else happens and your tabs stay as they are.

Downloaded packages are kept in the `updates` folder in Claudette's data folder, and a package that's already downloaded isn't fetched again. Packages of the running version and older are deleted at launch.

## Upgrading from 0.1.0

0.1.0 came from before Claudette's repository moved. It announces later versions, with a link to their page, but can't install them. Install the new version by hand from the [Downloads page](../downloads.md). On Windows the new MSIX installs beside 0.1.0's.

## Settings

All in Settings → General, under **Updates and status**:

- **Check for Claudette updates automatically**: on by default.
- **Include pre-releases**: off by default.
- **Check now**, which also shows the version and when Claudette last checked.

The version is also at the foot of the Settings sidebar.

## See also

- [Claude Code's updates](claude-codes-updates.md), for the program Claudette runs.
- [What leaves this computer](../privacy/what-leaves-this-computer.md), for what an update check sends.
