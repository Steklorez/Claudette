#!/usr/bin/env bash
# Builds Claudette.app and a .dmg for one architecture (DESIGN.md §2, "Packaging"), then signs and notarizes them when
# credentials are given. Without them it still builds, unsigned, which is what pull request checks do.
#
#   packaging/macos/build-dmg.sh <version> <arm64|x64> [output folder]
#
# Signing:       MACOS_SIGNING_IDENTITY  "Developer ID Application: Name (TEAMID)", or its SHA-1 hash, in the default or an
#                                        unlocked keychain
# Notarization:  APPLE_API_KEY_PATH, APPLE_API_KEY_ID, APPLE_API_ISSUER   (an App Store Connect API key), or
#                APPLE_ID, APPLE_TEAM_ID, APPLE_APP_PASSWORD             (an app-specific password)
#
# Off macOS (no iconutil, codesign or hdiutil) it stops after building the .app, which checks the bundle layout.
set -euo pipefail

version="${1:?version, such as 1.2.3}"
arch="${2:?arm64 or x64}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="${3:-$root/artifacts}"
here="$root/packaging/macos"
work="$root/obj/package/osx-$arch"
app="$work/Claudette.app"
mkdir -p "$output"
rm -rf "$work"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

echo "Publishing Claudette $version for osx-$arch"
dotnet publish "$root/src/Claudette.App/Claudette.App.csproj" -c Release -r "osx-$arch" --self-contained \
  -p:Version="$version" -p:DebugType=portable -o "$app/Contents/MacOS"
# The symbols stay out of the bundle (and its signature), kept beside it to read crash reports' stack traces with.
symbols="$output/symbols-osx-$arch"
rm -rf "$symbols"
mkdir -p "$symbols"
find "$app/Contents/MacOS" -maxdepth 1 -name '*.pdb' -exec mv {} "$symbols/" \;
sed "s/[$]VERSION[$]/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

if ! command -v iconutil >/dev/null; then
  echo "Not on macOS: built $app without an icon, signature or .dmg."
  exit 0
fi

# The icon, from the 1024-pixel master.
iconset="$work/Claudette.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$root/packaging/icon/claudette-1024.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$root/packaging/icon/claudette-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/Claudette.icns"

identity="${MACOS_SIGNING_IDENTITY:-}"
if [[ -n "$identity" ]]; then
  echo "Signing with $identity"
  # Inside out, with the hardened runtime notarization requires: every file beside the executable, then the bundle.
  # .NET keeps its assemblies there too, in Contents/MacOS, where codesign counts every file as code, so each is signed,
  # not only the Mach-O ones; one that isn't Mach-O keeps its signature in extended attributes, which the .dmg and
  # ditto keep. The entitlements, for .NET's JIT, go on the executable alone, which signing the bundle signs.
  find "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/Claudette" -print0 \
    | xargs -0 codesign --force --timestamp --options runtime --sign "$identity"
  codesign --force --timestamp --options runtime --entitlements "$here/Claudette.entitlements" --sign "$identity" "$app"
  codesign --verify --deep --strict --verbose=2 "$app"
else
  echo "warning: MACOS_SIGNING_IDENTITY isn't set, so the app is unsigned and Gatekeeper will block it." >&2
fi

dmg="$output/Claudette-$version-$arch.dmg"
staging="$work/dmg"
mkdir -p "$staging"
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "Claudette" -srcfolder "$staging" -ov -format UDZO "$dmg"

if [[ -n "$identity" ]]; then
  codesign --force --timestamp --sign "$identity" "$dmg"
  notary=()
  if [[ -n "${APPLE_API_KEY_PATH:-}" ]]; then
    notary=(--key "$APPLE_API_KEY_PATH" --key-id "$APPLE_API_KEY_ID" --issuer "$APPLE_API_ISSUER")
  elif [[ -n "${APPLE_ID:-}" ]]; then
    notary=(--apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD")
  fi
  if (( ${#notary[@]} )); then
    echo "Notarizing $dmg"
    submission="$work/notarization.json"
    xcrun notarytool submit "$dmg" "${notary[@]}" --wait --output-format json > "$submission" || true
    status="$(plutil -extract status raw "$submission" 2>/dev/null || true)"
    if [[ "$status" != "Accepted" ]]; then
      # Apple's log says which file it turned down, and why.
      echo "error: notarization ended ${status:-without a status}." >&2
      cat "$submission" >&2
      if submission_id="$(plutil -extract id raw "$submission" 2>/dev/null)"; then
        xcrun notarytool log "$submission_id" "${notary[@]}" >&2 || true
      fi
      exit 1
    fi
    xcrun stapler staple "$dmg"
    # Gatekeeper's own check, the one a downloaded .dmg gets.
    spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg"
  else
    echo "warning: no notarization credentials, so the .dmg isn't notarized." >&2
  fi
fi
echo "Built $dmg"
