#!/usr/bin/env python3
"""
Writes cli-manifest.json for a published NodeKit CLI directory (P09 profile:
linux-x64 self-contained, loose directory). Called by scripts/package-cli.sh.

The manifest starts from release/cli-manifest.template.json and fills in:
- version, source sha/dirty, contract source sha256, dotnet SDK version
- runtime_closure: every "package" library in NodeKit.Cli.deps.json
- files: every file in the publish directory (path, size, sha256, executable)

It fails (exit 1) instead of writing a manifest when:
- the entrypoint apphost is missing or not executable
- NodeKit.Cli.deps.json is missing
- the runtime closure contains a GUI/K8s package (Avalonia, SkiaSharp,
  Wasmtime, KubernetesClient). The CLI must not ship those.

Exit codes: 0 written, 1 rejected, 2 usage error.
"""

import argparse
import hashlib
import json
import os
import sys

FORBIDDEN_PREFIXES = ("Avalonia", "SkiaSharp", "Wasmtime", "KubernetesClient")
MANIFEST_NAME = "cli-manifest.json"
DEPS_NAME = "NodeKit.Cli.deps.json"


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def list_files(publish_dir):
    entries = []
    for root, dirs, files in os.walk(publish_dir):
        dirs.sort()
        for name in sorted(files):
            full = os.path.join(root, name)
            rel = os.path.relpath(full, publish_dir).replace(os.sep, "/")
            if rel == MANIFEST_NAME:
                continue
            entries.append({
                "path": rel,
                "size": os.path.getsize(full),
                "sha256": sha256_file(full),
                "executable": os.access(full, os.X_OK),
            })
    entries.sort(key=lambda e: e["path"])
    return entries


def runtime_closure(deps_path):
    with open(deps_path, "r", encoding="utf-8") as f:
        deps = json.load(f)
    closure = []
    for key, lib in deps.get("libraries", {}).items():
        if lib.get("type") != "package":
            continue
        name, _, version = key.partition("/")
        closure.append({"name": name, "version": version})
    closure.sort(key=lambda e: (e["name"].lower(), e["version"]))
    return closure


def fail(message):
    sys.stderr.write("write-cli-manifest: " + message + "\n")
    return 1


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--template", required=True)
    parser.add_argument("--publish-dir", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--source-dirty", choices=("true", "false"), required=True)
    parser.add_argument("--contract-source", required=True)
    parser.add_argument("--dotnet-sdk", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)

    with open(args.template, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    entrypoint = os.path.join(args.publish_dir, manifest["entrypoint"])
    if not os.path.isfile(entrypoint):
        return fail("entrypoint %s is missing from %s" % (manifest["entrypoint"], args.publish_dir))
    if not os.access(entrypoint, os.X_OK):
        return fail("entrypoint %s is not executable" % manifest["entrypoint"])

    deps_path = os.path.join(args.publish_dir, DEPS_NAME)
    if not os.path.isfile(deps_path):
        return fail("%s is missing from %s" % (DEPS_NAME, args.publish_dir))
    closure = runtime_closure(deps_path)
    forbidden = [e["name"] for e in closure if e["name"].startswith(FORBIDDEN_PREFIXES)]
    if forbidden:
        return fail("runtime closure contains GUI/K8s packages: " + ", ".join(forbidden))

    manifest["version"] = args.version
    manifest["source"]["sha"] = args.source_sha
    manifest["source"]["dirty"] = args.source_dirty == "true"
    manifest["contract_source"]["sha256"] = sha256_file(args.contract_source)
    manifest["build"]["dotnet_sdk"] = args.dotnet_sdk
    manifest["runtime_closure"] = closure
    manifest["files"] = list_files(args.publish_dir)

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, sort_keys=False)
        f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
