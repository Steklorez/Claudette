#!/bin/sh
# Claudette's update helper for macOS (DESIGN.md §2, "Updating Claudette"). Claudette starts it, detached, once it has
# closed its tabs and written the restart snapshot, then quits. The helper:
#
#   1. waits for Claudette to quit,
#   2. puts the new app in place, with the old one set aside (all in the same folder, so each move is a rename): under
#      the old one's name, or under its own when the app was renamed, as Claudette.app becomes DevEntry.app,
#   3. starts the new version with the restart's arguments, and waits up to 60 seconds for it to write the nonce to the
#      ready file; once it has, removes what it set aside, the old app among it,
#   4. if it doesn't, stops it, puts everything back and starts the old version with the same arguments: it takes the
#      tabs back from the snapshot, as a source build does when a new build fails (DESIGN.md §9).
#
#   update-helper.sh <pid> <app> <staged app> <new app> <ready file> <nonce> <open|exec> <restart arguments...>
#
# <new app> is where the new version goes: <app>, or its new name beside it. "open" starts an app through Launch
# Services; "exec" runs its executable, named as its bundle is, directly, for tests off macOS. Tests also shorten the
# waits with CLAUDETTE_UPDATE_QUIT_TICKS and CLAUDETTE_UPDATE_READY_TICKS.
set -u

pid="$1"; app="$2"; staged="$3"; new="$4"; ready="$5"; nonce="$6"; mode="$7"
shift 7
previous="$app.previous"
# A copy already where a renamed app goes, such as one installed by hand: set aside too, and back if the update fails.
displaced=""
launched=""
quit_ticks="${CLAUDETTE_UPDATE_QUIT_TICKS:-1200}"    # of 0.1 s: 2 minutes
ready_ticks="${CLAUDETTE_UPDATE_READY_TICKS:-240}"   # of 0.25 s: 60 seconds

launch() {
  target="$1"; shift
  if [ "$mode" = open ]; then
    /usr/bin/open -n "$target" --args "$@"
  else
    "$target/Contents/MacOS/$(basename "$target" .app)" "$@" >/dev/null 2>&1 &
    launched=$!
  fi
}

# Puts back what was set aside, the old app last, and starts it.
go_back() {
  if [ -n "$displaced" ]; then
    mv "$displaced" "$new"
  fi
  mv "$previous" "$app"
  launch "$app" "$@"
  exit 1
}

# 1. Wait up to 2 minutes for Claudette to quit.
i=0
while kill -0 "$pid" 2>/dev/null; do
  i=$((i + 1))
  if [ "$i" -ge "$quit_ticks" ]; then
    echo "Claudette didn't quit, so the update wasn't installed." >&2
    exit 1
  fi
  sleep 0.1
done

# 2. Put the new app in place. If a move fails, what moved goes back, and the old version starts again.
rm -rf "$previous"
if ! mv "$app" "$previous"; then
  launch "$app" "$@"
  exit 1
fi
if [ "$new" != "$app" ] && [ -e "$new" ]; then
  rm -rf "$new.previous"
  if ! mv "$new" "$new.previous"; then
    go_back "$@"
  fi
  displaced="$new.previous"
fi
if ! mv "$staged" "$new"; then
  go_back "$@"
fi

# 3. Start the new version and wait for it to say it's up.
rm -f "$ready"
launch "$new" "$@"
i=0
while [ "$i" -lt "$ready_ticks" ]; do
  if [ "$(cat "$ready" 2>/dev/null)" = "$nonce" ]; then
    rm -rf "$previous"
    if [ -n "$displaced" ]; then
      rm -rf "$displaced"
    fi
    rm -f "$ready"
    exit 0
  fi
  i=$((i + 1))
  sleep 0.25
done

# 4. It didn't start: stop it, remove it and go back.
if [ -n "$launched" ]; then
  kill "$launched" 2>/dev/null
else
  pkill -f "$new/Contents/MacOS/" 2>/dev/null
fi
sleep 1
rm -rf "$new"
go_back "$@"
