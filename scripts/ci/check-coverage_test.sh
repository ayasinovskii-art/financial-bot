#!/usr/bin/env bash
# Runnable unit test for check-coverage.sh. Run: bash scripts/ci/check-coverage_test.sh
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
SUT="$HERE/check-coverage.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
fail=0

mk() { printf '<coverage line-rate="%s"></coverage>\n' "$1" > "$tmp/$2"; }

# Above threshold -> exit 0
mk 0.81 high.xml
if MIN_COVERAGE=0.70 bash "$SUT" "$tmp/high.xml" >/dev/null; then echo "ok: above passes"; else echo "FAIL: above should pass"; fail=1; fi

# Below threshold -> exit non-zero
mk 0.50 low.xml
if MIN_COVERAGE=0.70 bash "$SUT" "$tmp/low.xml" >/dev/null; then echo "FAIL: below should fail"; fail=1; else echo "ok: below fails"; fi

# Missing file -> exit non-zero
if MIN_COVERAGE=0.70 bash "$SUT" "$tmp/nope.xml" >/dev/null 2>&1; then echo "FAIL: missing should fail"; fail=1; else echo "ok: missing fails"; fi

# Large multi-match report (real ReportGenerator output has thousands of line-rate
# attrs): must read the ROOT (first) line-rate without breaking the pipe.
{ printf '<coverage line-rate="0.81">\n'; for i in $(seq 1 5000); do printf '<class line-rate="0.50"/>\n'; done; printf '</coverage>\n'; } > "$tmp/big.xml"
if MIN_COVERAGE=0.70 bash "$SUT" "$tmp/big.xml" >/dev/null; then echo "ok: big multi-match passes (reads root)"; else echo "FAIL: big multi-match should pass"; fail=1; fi

exit $fail
