#!/usr/bin/env bash
# Decide what this tick should do. Emits to $GITHUB_OUTPUT (or stdout when run locally):
#   stage=<analysis|planning|coding|qa|security|review|manager|idle>
#   issue=<number or empty>
# Rules:
#   1. If an open issue carries a stage:* label (not done/blocked) -> that stage
#      (lowest number wins if several).
#   2. Else pick the lowest-numbered READY issue with bot:queue -> stage=manager.
#      "Ready" means: authored by the repo owner or a *[bot] account (so random
#      users can't burn tokens), AND every `Depends-on: #N` it declares is closed
#      (so a task never blocks waiting on an unimplemented prerequisite).
#   3. Else if MANAGER_INVENTS=1 -> stage=manager with empty issue.
#   4. Else idle.
set -euo pipefail
out="${GITHUB_OUTPUT:-/dev/stdout}"
OWNER="${OWNER:-${GITHUB_REPOSITORY_OWNER:-}}"

# ---- Step 1: an already-adopted issue in some stage:* takes priority ----------
active="$(gh issue list --state open --json number,labels \
  --jq '[ .[] | {n:.number, st:([.labels[].name | select(startswith("stage:")) | sub("stage:";"")] | first)}
         | select(.st != null and .st != "done" and .st != "blocked") ]')"

count="$(jq 'length' <<<"$active")"
if [[ "$count" -gt 1 ]]; then
  echo "::warning::more than one active issue; taking the lowest number"
fi

if [[ "$count" -ge 1 ]]; then
  n="$(jq -r 'sort_by(.n) | .[0].n' <<<"$active")"
  st="$(jq -r 'sort_by(.n) | .[0].st' <<<"$active")"
  { echo "stage=$st"; echo "issue=$n"; } >> "$out"
  exit 0
fi

# ---- Step 2: pick a READY queued issue (author-gated + dependency-aware) -------
open_nums="$(gh issue list --state open --json number --jq '[.[].number]')"
queued_json="$(gh issue list --state open --label bot:queue --json number,author,body)"

queued="$(jq -r --arg owner "$OWNER" --argjson open "$open_nums" '
  [ .[]
    # author gate: repo owner or any GitHub App / bot account
    | select(.author.login == $owner or (.author.login | endswith("[bot]")))
    # parse "Depends-on: #N" markers (case-insensitive, comma/space separated)
    | { n: .number,
        deps: [ (.body // "") | split("\n")[]
                | select(test("depends[- ]on"; "i"))
                | scan("#([0-9]+)") | .[0] | tonumber ] }
    # ready only if every declared dependency is already closed (not in open set)
    | select( all(.deps[]; . as $d | ($open | index($d)) == null) )
  ] | sort_by(.n) | .[0].n // empty' <<<"$queued_json")"

if [[ -n "$queued" || "${MANAGER_INVENTS:-0}" == "1" ]]; then
  { echo "stage=manager"; echo "issue=${queued:-}"; } >> "$out"
  exit 0
fi

{ echo "stage=idle"; echo "issue="; } >> "$out"
