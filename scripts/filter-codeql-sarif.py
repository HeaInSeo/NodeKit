#!/usr/bin/env python3
"""
Removes CodeQL SARIF results whose primary location is a generated file
under obj/ that this repository does not track in git.

Why this exists: NodeKit's CodeQL workflow runs a real `dotnet build` and
traces it (the default, higher-fidelity build mode) rather than
build-mode: none, and per GitHub's own docs, the paths-ignore
configuration option only filters results for a compiled language "when
you analyze [it] without building the code" - i.e. build-mode: none only.
Under build tracing, paths-ignore is silently inert; confirmed
empirically (2026-07-16): obj/-only rule alert counts were unchanged
before/after adding a paths-ignore config file. Since we deliberately
keep full build tracing (source-linked files, multi-project references,
and real compiled-protobuf cross-references all depend on it), the fix
is not build-mode: none - it's filtering the SARIF results themselves
after analysis, before they're uploaded.

Safety conditions - a result is ONLY removed if BOTH hold:
  - its primary physical location path contains an "obj" path segment
    (i.e. matches **/obj/**)
  - that exact path is NOT tracked by `git ls-files` in this repository

Anything else is left in the SARIF untouched. The script fails loudly
(non-zero exit, clear stderr message) rather than silently doing the
wrong thing if:
  - a result that WOULD be removed turns out to reference a tracked file
  - a result outside **/obj/** would be removed
  - the SARIF file can't be parsed
  - the filtered SARIF is structurally invalid (missing "runs", wrong
    top-level type, etc.)

Only obj/ is in scope for now - bin/ is deliberately not touched.
"""

import json
import os
import subprocess
import sys
import urllib.parse


def is_obj_path(path):
    parts = path.replace("\\", "/").split("/")
    return "obj" in parts


def git_tracked_files(repo_root):
    result = subprocess.run(
        ["git", "-C", repo_root, "ls-files"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        universal_newlines=True,
    )
    if result.returncode != 0:
        sys.stderr.write("git ls-files failed: %s\n" % result.stderr)
        sys.exit(1)
    return set(result.stdout.splitlines())


def result_primary_path(result):
    locations = result.get("locations") or []
    if not locations:
        return None
    physical = locations[0].get("physicalLocation") or {}
    artifact = physical.get("artifactLocation") or {}
    uri = artifact.get("uri")
    if uri is None:
        return None
    return urllib.parse.unquote(uri)


def filter_sarif(sarif, tracked_files):
    if not isinstance(sarif, dict) or "runs" not in sarif or not isinstance(sarif["runs"], list):
        sys.stderr.write("SARIF is structurally invalid: no top-level 'runs' array.\n")
        sys.exit(1)

    stats = {
        "before": 0,
        "after": 0,
        "removed_by_rule": {},
        "removed_paths": [],
        "source_controlled_removed": 0,
    }

    for run in sarif["runs"]:
        results = run.get("results") or []
        stats["before"] += len(results)

        kept = []
        for result in results:
            path = result_primary_path(result)
            rule_id = result.get("ruleId", "<unknown>")

            if path is not None and is_obj_path(path) and path not in tracked_files:
                # Eligible for removal under the two safety conditions.
                stats["removed_by_rule"][rule_id] = stats["removed_by_rule"].get(rule_id, 0) + 1
                stats["removed_paths"].append(path)
                continue

            if path is not None and is_obj_path(path) and path in tracked_files:
                # A generated obj/ file that IS tracked would be unexpected
                # (obj/ is gitignored) - don't silently drop it, something
                # about our assumptions is wrong.
                sys.stderr.write(
                    "SAFETY VIOLATION: refusing to remove a git-tracked file "
                    "under obj/: %s (rule %s)\n" % (path, rule_id)
                )
                stats["source_controlled_removed"] += 1

            kept.append(result)

        run["results"] = kept
        stats["after"] += len(kept)

    if stats["source_controlled_removed"] > 0:
        sys.stderr.write(
            "Aborting: %d result(s) would have removed a tracked file.\n"
            % stats["source_controlled_removed"]
        )
        sys.exit(1)

    for path in stats["removed_paths"]:
        if not is_obj_path(path):
            sys.stderr.write("SAFETY VIOLATION: removed a non-obj/ path: %s\n" % path)
            sys.exit(1)

    return sarif, stats


def main():
    if len(sys.argv) != 3:
        sys.stderr.write("usage: filter-codeql-sarif.py <input.sarif> <output.sarif>\n")
        return 1

    input_path, output_path = sys.argv[1], sys.argv[2]
    repo_root = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        universal_newlines=True,
    ).stdout.strip()
    if not repo_root:
        sys.stderr.write("Could not determine git repository root.\n")
        return 1

    if not os.path.isfile(input_path):
        sys.stderr.write("SARIF file not found: %s\n" % input_path)
        return 1

    try:
        with open(input_path) as f:
            sarif = json.load(f)
    except ValueError as exc:
        sys.stderr.write("Could not parse SARIF file %s: %s\n" % (input_path, exc))
        return 1

    tracked_files = git_tracked_files(repo_root)
    filtered, stats = filter_sarif(sarif, tracked_files)

    with open(output_path, "w") as f:
        json.dump(filtered, f)

    removed = stats["before"] - stats["after"]
    print("Alerts before filtering: %d" % stats["before"])
    print("Alerts after filtering: %d" % stats["after"])
    print("Removed: %d" % removed)
    print("Removed by rule:")
    for rule_id, count in sorted(stats["removed_by_rule"].items(), key=lambda kv: -kv[1]):
        print("  %4d  %s" % (count, rule_id))
    print("Removed file paths:")
    for path in sorted(set(stats["removed_paths"])):
        print("  %s" % path)
    print("Source-controlled files removed: %d" % stats["source_controlled_removed"])

    return 0


if __name__ == "__main__":
    sys.exit(main())
