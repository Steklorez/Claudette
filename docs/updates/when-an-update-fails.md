---
title: When an update fails
parent: Updates
nav_order: 3
summary: "Claudette updates in steps, so you can tell where one went wrong."
---

# When an update fails

Claudette updates in steps, so you can tell where one went wrong. Before it closes, a failure leaves everything as it was. After it closes, it goes back to the old version when it can.

## Before Claudette closes

The package fails a check, Windows refuses it, or the macOS helper can't start. Your tabs come straight back, and the update card says why, in a box under the notes. Nothing else happens.

If the package is fine but couldn't be installed this way, the card offers a way to install it yourself:

- **Open the installer** on Windows opens the package in App Installer.
- **Open the disk image** on macOS opens the `.dmg`.

## Moving Claudette to Applications

macOS runs an app opened from Downloads from a temporary, read-only copy, and that copy can't be updated. The card asks you to move Claudette to Applications, or to install by hand.

1. Quit Claudette.
2. Drag `Claudette.app` to your Applications folder.
3. Open it from Applications.

## After Claudette closes

- **macOS, the new version doesn't start.** The helper stops it, puts the old app back and starts that. The old app takes the tabs back from the snapshot, which is still there, and says the update didn't finish.
- **Windows, the new version doesn't start.** The snapshot waits for a day. The next time you start Claudette within that day, it takes your tabs from the snapshot, even without a restart request, and says whether the update installed.

## Installing by hand

1. Download the package for your system from the [Downloads page](../downloads.md).
2. Quit Claudette.
3. On Windows, open the `.msix` and follow App Installer's prompts. It replaces your installed Claudette. On macOS, open the `.dmg` and drag `Claudette.app` onto the Applications link, replacing the one there.
4. Start Claudette.

Your settings, saved tabs and history are in Claudette's own folders, so they're still there. Pinned tabs come back, and so do unpinned ones when Settings → General → **Also restore unpinned tabs when Claudette starts** is on: see [Closing and restoring](../tabs/closing-and-restoring.md).

## Other causes

- **No package for your computer.** *This release has no package for this computer yet. See its release page.* The release may still be uploading, or may not cover your system. Check the [Downloads page](../downloads.md).
- **No checksum.** *GitHub published no checksum for this release's package, so Claudette can't check it.* Download it from the release page and install it by hand.
- **A copy that can't update itself.** Install the new version by hand.
- **Upgrading from 0.1.0.** 0.1.0 can't install later versions at all: see [Claudette's updates](claudettes-updates.md#upgrading-from-010).

If an update keeps failing, [Reporting an issue](../troubleshooting/reporting-an-issue.md) says what to include.

## See also

- [Claudette's updates](claudettes-updates.md)
- [Diagnostics and logs](../troubleshooting/diagnostics-and-logs.md)
