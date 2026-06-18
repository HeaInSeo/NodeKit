#!/usr/bin/env bash
set -euo pipefail

coverage_file="${1:-}"
minimum_line_rate="${MINIMUM_LINE_RATE:-0.1400}"
minimum_branch_rate="${MINIMUM_BRANCH_RATE:-0.0900}"

if [[ -z "$coverage_file" ]]; then
  coverage_file="$(find TestResults -path '*/coverage.cobertura.xml' -type f -printf '%T@ %p\n' | sort -nr | awk 'NR == 1 { print $2 }')"
fi

if [[ -z "$coverage_file" || ! -f "$coverage_file" ]]; then
  printf 'coverage.cobertura.xml not found.\n' >&2
  exit 1
fi

coverage_line="$(sed -n '2p' "$coverage_file")"
line_rate="$(sed -n 's/.*line-rate="\([^"]*\)".*/\1/p' <<<"$coverage_line")"
branch_rate="$(sed -n 's/.*branch-rate="\([^"]*\)".*/\1/p' <<<"$coverage_line")"

if [[ -z "$line_rate" || -z "$branch_rate" ]]; then
  printf 'Could not parse coverage rates from %s.\n' "$coverage_file" >&2
  exit 1
fi

printf 'Coverage file: %s\n' "$coverage_file"
printf 'Line coverage: %s, required: %s\n' "$line_rate" "$minimum_line_rate"
printf 'Branch coverage: %s, required: %s\n' "$branch_rate" "$minimum_branch_rate"

awk -v actual="$line_rate" -v minimum="$minimum_line_rate" 'BEGIN { exit(actual + 0 >= minimum + 0 ? 0 : 1) }' || {
  printf 'Line coverage is below threshold.\n' >&2
  exit 1
}

awk -v actual="$branch_rate" -v minimum="$minimum_branch_rate" 'BEGIN { exit(actual + 0 >= minimum + 0 ? 0 : 1) }' || {
  printf 'Branch coverage is below threshold.\n' >&2
  exit 1
}
