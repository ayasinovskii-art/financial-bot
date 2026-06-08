#!/usr/bin/env bash
# Decide what this tick should do. Emits to $GITHUB_OUTPUT (or stdout when run locally):
#   stage=<analysis|planning|coding|qa|security|review|manager|idle>
#   issue=<number or empty>
# Rules:
#   1. If exactly one open issue carries a stage:* label (not done/blocked) -> that stage.
#   2. Else if an open issue has bot:queue (or MANAGER_INVENTS=1) -> stage=manager.
#   3. Else idle.
set -euo pipefail
out="${GITHUB_OUTPUT:-/dev/stdout}"

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

queued="$(gh issue list --state open --label bot:queue --json number --jq 'sort_by(.number) | .[0].number // empty')"
if [[ -n "$queued" || "${MANAGER_INVENTS:-0}" == "1" ]]; then
  { echo "stage=manager"; echo "issue=${queued:-}"; } >> "$out"
  exit 0
fi

{ echo "stage=idle"; echo "issue="; } >> "$out"
