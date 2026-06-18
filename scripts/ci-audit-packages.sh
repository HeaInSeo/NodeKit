#!/usr/bin/env bash
set -euo pipefail

projects=(
  "NodeKit.csproj"
  "tests/NodeKit.Tests/NodeKit.Tests.csproj"
)

for project in "${projects[@]}"; do
  vulnerable_output="$(dotnet list "$project" package --vulnerable --include-transitive)"
  printf '%s\n' "$vulnerable_output"
  if grep -q "has the following vulnerable packages" <<<"$vulnerable_output"; then
    printf 'Vulnerable NuGet packages detected in %s.\n' "$project" >&2
    exit 1
  fi

  deprecated_output="$(dotnet list "$project" package --deprecated)"
  printf '%s\n' "$deprecated_output"
  if grep -q "has the following deprecated packages" <<<"$deprecated_output"; then
    printf 'Deprecated NuGet packages detected in %s. Review and migrate in a dedicated dependency update.\n' "$project" >&2
  fi
done
