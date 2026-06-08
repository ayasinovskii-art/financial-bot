#!/usr/bin/env bash
# Manage the <!-- pipeline-state ... --> JSON block inside an issue body.
#   state.sh read              < body.md   -> prints the JSON (empty if none)
#   state.sh write '<json>'    < body.md   -> prints body with block replaced/appended
set -euo pipefail
BEGIN='<!-- pipeline-state'
END='-->'
cmd="${1:?usage: state.sh read|write [json]}"
body="$(cat)"

extract() {
  awk -v b="$BEGIN" -v e="$END" '
    index($0,b){grab=1; next}
    grab && index($0,e){grab=0; next}
    grab{print}
  ' <<<"$body"
}

case "$cmd" in
  read)
    extract | sed '/^[[:space:]]*$/d'
    ;;
  write)
    json="${2:?usage: state.sh write <json>}"
    block=$'\n'"$BEGIN"$'\n'"$json"$'\n'"$END"$'\n'
    if grep -qF "$BEGIN" <<<"$body"; then
      # Replace existing block (from BEGIN line through the next END line).
      awk -v b="$BEGIN" -v e="$END" -v repl="$json" '
        index($0,b){print b; print repl; skip=1; next}
        skip && index($0,e){print e; skip=0; next}
        skip{next}
        {print}
      ' <<<"$body"
    else
      printf '%s\n%s' "$body" "$block"
    fi
    ;;
  *) echo "unknown command: $cmd" >&2; exit 2;;
esac
