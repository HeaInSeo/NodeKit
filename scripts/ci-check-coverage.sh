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

# 전체 커버리지 하한(위)은 낮게 잡혀 있어 회귀 방어력이 약하다. 재현성/제출
# 경로의 핵심 클래스는 별도의 더 높은 기준으로 지킨다. 임계값은 실측치
# (2026-07 기준: GrpcToolSpecClient branch-rate 0.5555가 가장 낮음)에 약간의
# 여유를 둔 것이지 이상적인 목표치가 아니다 — GrpcToolSpecClient의 branch
# coverage가 현재 병목이며, 향후 테스트 추가로 끌어올릴 여지가 있다.
declare -A core_classes=(
  ["NodeKit.Validation.Recipes.RecipeValidationPipeline"]=1
  ["NodeKit.Authoring.Recipes.RecipeRenderer"]=1
  ["NodeKit.Cli.SubmitCommand"]=1
  ["NodeKit.Cli.GrpcToolSpecClient"]=1
  ["NodeKit.Cli.HarborImageDigestResolver"]=1
)
core_minimum_line_rate="${CORE_MINIMUM_LINE_RATE:-0.70}"
core_minimum_branch_rate="${CORE_MINIMUM_BRANCH_RATE:-0.50}"

for class_name in "${!core_classes[@]}"; do
  class_escaped="${class_name//./\\.}"
  class_attrs="$(grep -oP "(?<=<class )[^>]*name=\"${class_escaped}\"" "$coverage_file" || true)"

  if [[ -z "$class_attrs" ]]; then
    printf 'Core class not found in coverage report: %s\n' "$class_name" >&2
    exit 1
  fi

  class_line_rate="$(grep -oP '(?<=line-rate=")[^"]*' <<<"$class_attrs")"
  class_branch_rate="$(grep -oP '(?<=branch-rate=")[^"]*' <<<"$class_attrs")"

  printf 'Core class: %s (line-rate: %s, branch-rate: %s)\n' "$class_name" "$class_line_rate" "$class_branch_rate"

  awk -v actual="$class_line_rate" -v minimum="$core_minimum_line_rate" 'BEGIN { exit(actual + 0 >= minimum + 0 ? 0 : 1) }' || {
    printf 'Line coverage for %s is below the core-class threshold (%s < %s).\n' "$class_name" "$class_line_rate" "$core_minimum_line_rate" >&2
    exit 1
  }

  awk -v actual="$class_branch_rate" -v minimum="$core_minimum_branch_rate" 'BEGIN { exit(actual + 0 >= minimum + 0 ? 0 : 1) }' || {
    printf 'Branch coverage for %s is below the core-class threshold (%s < %s).\n' "$class_name" "$class_branch_rate" "$core_minimum_branch_rate" >&2
    exit 1
  }
done
