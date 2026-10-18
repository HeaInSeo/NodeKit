#!/usr/bin/env python3
"""
Checks the NodeKit CLI dependency closure (S4-01-B). Called by
scripts/verify-cli-clean-build.sh after a clean CLI-only Release build.

Inputs:
- --lock: src/NodeKit.Cli/packages.lock.json (every target section, incl. linux-x64)
- --deps: NodeKit.Cli.deps.json from the CLI build output (runtime closure)

It fails (exit 1) when:
- the lock file or the runtime closure contains a GUI/K8s package (Avalonia,
  SkiaSharp, Wasmtime, KubernetesClient). Same list as write-cli-manifest.py.
- the lock file has a project dependency (the CLI must not reference NodeKit.csproj)
- a required runtime package (Spectre.Console, Grpc.Net.Client, Google.Protobuf)
  is not a Direct lock dependency or is missing from the runtime closure

On success it prints the runtime closure (name/version), one per line.

Exit codes: 0 ok, 1 rejected, 2 usage error.
"""

import argparse
import json
import sys

FORBIDDEN_PREFIXES = ("Avalonia", "SkiaSharp", "Wasmtime", "KubernetesClient")
REQUIRED_RUNTIME = ("Spectre.Console", "Grpc.Net.Client", "Google.Protobuf")


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def lock_entries(lock):
    """Yields (section, name, type) for every dependency in every target section."""
    for section, deps in sorted(lock.get("dependencies", {}).items()):
        for name, entry in sorted(deps.items()):
            yield section, name, entry.get("type", "")


def runtime_closure(deps):
    closure = {}
    for key, lib in deps.get("libraries", {}).items():
        if lib.get("type") != "package":
            continue
        name, _, version = key.partition("/")
        closure[name] = version
    return closure


def check(lock, deps):
    """Returns (problems, closure)."""
    problems = []
    direct = set()
    for section, name, kind in lock_entries(lock):
        if name.startswith(FORBIDDEN_PREFIXES):
            problems.append("lock %s contains GUI/K8s package %s" % (section, name))
        if kind.lower() == "project":
            problems.append("lock %s has project dependency %s" % (section, name))
        if kind == "Direct":
            direct.add(name)
    closure = runtime_closure(deps)
    for name in sorted(closure):
        if name.startswith(FORBIDDEN_PREFIXES):
            problems.append("runtime closure contains GUI/K8s package %s" % name)
    for name in REQUIRED_RUNTIME:
        if name not in direct:
            problems.append("%s is not a Direct dependency in the lock file" % name)
        if name not in closure:
            problems.append("%s is missing from the runtime closure" % name)
    return problems, closure


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--lock", required=True)
    parser.add_argument("--deps", required=True)
    args = parser.parse_args(argv)

    problems, closure = check(load_json(args.lock), load_json(args.deps))
    if problems:
        for p in problems:
            sys.stderr.write("check-cli-closure: " + p + "\n")
        return 1
    for name in sorted(closure, key=str.lower):
        print("%s %s" % (name, closure[name]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
