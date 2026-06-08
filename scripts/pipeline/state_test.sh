#!/usr/bin/env bash
# Run: bash scripts/pipeline/state_test.sh
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
SUT="$HERE/state.sh"
fail=0
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

# read: extract JSON from a body that has the block
cat > "$tmp/body.md" <<'EOF'
Some human text.

<!-- pipeline-state
{"stage":"coding","qa_iterations":1}
-->

More text.
EOF
got="$(bash "$SUT" read < "$tmp/body.md")"
if echo "$got" | grep -q '"stage":"coding"'; then echo "ok: read extracts json"; else echo "FAIL: read got [$got]"; fail=1; fi

# read: empty when no block
echo "no block here" > "$tmp/none.md"
got="$(bash "$SUT" read < "$tmp/none.md")"
if [[ -z "$got" ]]; then echo "ok: read empty when absent"; else echo "FAIL: expected empty, got [$got]"; fail=1; fi

# write: replace block, preserve surrounding text
new='{"stage":"qa","qa_iterations":2}'
out="$(bash "$SUT" write "$new" < "$tmp/body.md")"
if echo "$out" | grep -q '"stage":"qa"' && echo "$out" | grep -q "Some human text" && echo "$out" | grep -q "More text"; then
  echo "ok: write replaces block, keeps text"; else echo "FAIL: write output wrong"; echo "$out"; fail=1; fi

# write: append block when none exists
out="$(bash "$SUT" write "$new" < "$tmp/none.md")"
if echo "$out" | grep -q "no block here" && echo "$out" | grep -q '"stage":"qa"'; then
  echo "ok: write appends when absent"; else echo "FAIL: append failed"; echo "$out"; fail=1; fi

exit $fail
