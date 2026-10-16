#!/usr/bin/env bash
# Prints a version's release notes: its section of CHANGELOG.md, without the heading (DESIGN.md §2, "Packaging and
# signing"; GUIDE.md, "The changelog"). package.yml drafts each release with them, so the release on GitHub and
# Claudette's update dialog say what the changelog says.
#
# A pre-release (1.4.0-beta.1) without a section of its own takes the Unreleased section. A version with no notes
# fails, so a tag can't draft a release without them.
#
#   packaging/release-notes.sh <version> [changelog]
set -euo pipefail

version="${1:?usage: release-notes.sh <version> [changelog]}"
changelog="${2:-$(dirname "$0")/../CHANGELOG.md}"

# The lines under "## [<name>]", up to the next version's heading or the links at the bottom, from the first that
# isn't blank. The command substitution that reads them drops the blank lines at the end.
section() {
  awk -v heading="## [$1]" '
    /^## / { if (found) exit; found = (index($0, heading) == 1); next }
    /^\[[^]]+\]: / { if (found) exit }
    found { print }
  ' "$changelog" | sed '/./,$!d'
}

notes="$(section "$version")"
if [[ -z "${notes//[[:space:]]/}" && "$version" == *-* ]]; then
  notes="$(section Unreleased)"
fi
if [[ -z "${notes//[[:space:]]/}" ]]; then
  echo "$changelog has no notes for $version. Add its section first (GUIDE.md, \"The changelog\", \"Releasing\")." >&2
  exit 1
fi
printf '%s\n' "$notes"
