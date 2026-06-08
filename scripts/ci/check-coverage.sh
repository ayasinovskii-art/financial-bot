#!/usr/bin/env bash
# Fail if combined line coverage is below MIN_COVERAGE (0..1, default 0.70).
# Usage: MIN_COVERAGE=0.70 check-coverage.sh <combined-cobertura.xml>
set -euo pipefail
report="${1:?usage: check-coverage.sh <cobertura.xml>}"
min="${MIN_COVERAGE:-0.70}"

if [[ ! -f "$report" ]]; then
  echo "::error::coverage report not found: $report" >&2
  exit 2
fi

# Cobertura stores fractional line coverage in the root <coverage line-rate="..">.
# Use grep -m1 (stop after the first match) so a large report with thousands of
# per-class line-rate attrs can't SIGPIPE the producer (which under pipefail aborts).
rate="$(grep -m1 -oE 'line-rate="[0-9.]+"' "$report" | grep -oE '[0-9.]+')"
if [[ -z "${rate:-}" ]]; then
  echo "::error::could not parse line-rate from $report" >&2
  exit 2
fi

pct=$(awk -v r="$rate" 'BEGIN{printf "%.1f", r*100}')
minpct=$(awk -v m="$min" 'BEGIN{printf "%.1f", m*100}')
echo "Line coverage: ${pct}% (minimum ${minpct}%)"

awk -v r="$rate" -v m="$min" 'BEGIN{exit !(r+1e-9 >= m)}' || {
  echo "::error::coverage ${pct}% is below minimum ${minpct}%" >&2
  exit 1
}
echo "Coverage gate passed."
