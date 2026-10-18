#!/usr/bin/env python3
"""
S4-05-C: checks the output of scripts/package-cli.sh before CI calls a package
run good. A missing or partial package must fail here, not pass because the
script exited 0.

For <dir>/nodekit-cli-<version>-<rid> it requires:
- the .tar.gz, its .sha256 (sha256sum -c format, hash matches the archive) and
  the .cli-manifest.json sidecar
- the archive holds exactly one top-level directory <name>/ and
  <name>/cli-manifest.json is byte-identical to the sidecar
- the manifest's version, profile.rid and source.dirty=false match this package
- the manifest entrypoint is a regular executable file in the archive
- every manifest files[] entry is in the archive with the same size, sha256 and
  executable flag, and the archive has no regular file the manifest omits

Exit codes: 0 PASS, 1 FAIL, 2 usage error.
"""

import argparse
import hashlib
import json
import os
import sys
import tarfile

MANIFEST_NAME = "cli-manifest.json"


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def check(package_dir, version, rid):
    """Returns a list of failure messages; empty means PASS."""
    name = "nodekit-cli-%s-%s" % (version, rid)
    archive = os.path.join(package_dir, name + ".tar.gz")
    checksum = archive + ".sha256"
    sidecar = os.path.join(package_dir, name + ".cli-manifest.json")

    missing = [p for p in (archive, checksum, sidecar) if not os.path.isfile(p)]
    if missing:
        return ["missing package output: " + os.path.basename(p) for p in missing]

    errors = []
    with open(checksum, "r", encoding="utf-8") as f:
        fields = f.read().split()
    if len(fields) != 2 or fields[1] != name + ".tar.gz":
        errors.append("%s is not '<sha256>  %s.tar.gz'" % (os.path.basename(checksum), name))
    elif fields[0] != sha256_file(archive):
        errors.append("%s does not match the archive hash" % os.path.basename(checksum))

    with open(sidecar, "rb") as f:
        sidecar_bytes = f.read()
    try:
        manifest = json.loads(sidecar_bytes)
    except ValueError as e:
        return errors + ["sidecar manifest is not JSON: %s" % e]

    if manifest.get("version") != version:
        errors.append("manifest version %r != %r" % (manifest.get("version"), version))
    if manifest.get("profile", {}).get("rid") != rid:
        errors.append("manifest profile.rid %r != %r" % (manifest.get("profile", {}).get("rid"), rid))
    if manifest.get("source", {}).get("dirty") is not False:
        errors.append("manifest source.dirty is not false")

    files = {}
    tops = set()
    with tarfile.open(archive, "r:gz") as tar:
        for member in tar.getmembers():
            top, _, rel = member.name.partition("/")
            tops.add(top)
            if member.isfile() and top == name and rel:
                data = tar.extractfile(member).read()
                files[rel] = {
                    "size": member.size,
                    "sha256": sha256_bytes(data),
                    "executable": bool(member.mode & 0o111),
                    "data": data if rel == MANIFEST_NAME else None,
                }
    if tops != {name}:
        errors.append("archive top-level entries %s != [%s]" % (sorted(tops), name))

    inner = files.pop(MANIFEST_NAME, None)
    if inner is None:
        errors.append("archive has no %s/%s" % (name, MANIFEST_NAME))
    elif inner["data"] != sidecar_bytes:
        errors.append("archive %s differs from the sidecar manifest" % MANIFEST_NAME)

    entrypoint = manifest.get("entrypoint")
    if not entrypoint or entrypoint not in files:
        errors.append("entrypoint %r is missing from the archive" % entrypoint)
    elif not files[entrypoint]["executable"]:
        errors.append("entrypoint %r is not executable in the archive" % entrypoint)

    listed = manifest.get("files") or []
    if not listed:
        errors.append("manifest files[] is empty")
    for entry in listed:
        path = entry.get("path")
        actual = files.get(path)
        if actual is None:
            errors.append("manifest file %r is missing from the archive" % path)
            continue
        for key in ("size", "sha256", "executable"):
            if entry.get(key) != actual[key]:
                errors.append("manifest file %r %s %r != archive %r" % (path, key, entry.get(key), actual[key]))
    extra = sorted(set(files) - {e.get("path") for e in listed})
    for path in extra:
        errors.append("archive file %r is not listed in the manifest" % path)
    return errors


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--dir", required=True, help="package-cli.sh --output directory")
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid", default="linux-x64")
    args = parser.parse_args(argv)

    errors = check(args.dir, args.version, args.rid)
    for message in errors:
        sys.stderr.write("verify-cli-package: FAIL: " + message + "\n")
    if errors:
        return 1
    print("verify-cli-package: PASS nodekit-cli-%s-%s (%s)" % (args.version, args.rid, args.dir))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
