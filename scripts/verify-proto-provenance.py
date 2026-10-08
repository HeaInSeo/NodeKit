#!/usr/bin/env python3
"""
Verify protos/provenance.json against the vendored NodeVault proto files.

The manifest is the machine-readable authority for every proto file the
NodeKit build compiles (protos/SOURCE.md is the human summary). This script
proves, from the NodeKit checkout alone:
  - every declared consumer file still has the declared SHA-256 digest,
  - the consumer bytes are the declared immutable producer bytes (the git
    blob id of the consumer file equals the producer blob id at the pinned
    full-length commit, and the SHA-256 digests agree),
  - every <Protobuf Include> in a project file is declared in the manifest,
    and the declared generator/runtime versions match those projects.

With --fetch-producer it additionally downloads the producer file at the
pinned commit (never a branch) and checks it against the declared digests.
With --producer-dir it does the same against a local producer checkout of
that commit. Neither mode consults the producer's moving default branch, so
an unchanged NodeKit commit gives the same verdict when NodeVault moves.

Deliberately written without f-strings/dataclasses/PEP604 type hints so
it also runs under old Python 3.6 interpreters, not just CI's modern one.
"""

import argparse
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

try:
    from urllib.request import urlopen
except ImportError:  # pragma: no cover - Python 2 is not supported
    urlopen = None


MANIFEST_RELATIVE_PATH = os.path.join("protos", "provenance.json")
SOURCE_DOC_RELATIVE_PATH = os.path.join("protos", "SOURCE.md")
API_PROTOS_ROOT_TOKEN = "$(ApiProtosRoot)"
VENDORED_PROTOS_DIR = "protos"
SKIPPED_DIRS = set([".git", "bin", "obj", "publish", "NodeKit_POC", "TestResults", "node_modules"])

FULL_SHA1 = re.compile(r"^[0-9a-f]{40}$")
FULL_SHA256 = re.compile(r"^[0-9a-f]{64}$")
GITHUB_REPOSITORY = re.compile(r"^https://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$")


def sha256_hex(data):
    return hashlib.sha256(data).hexdigest()


def git_blob_sha1_hex(data):
    header = ("blob %d\0" % len(data)).encode("ascii")
    return hashlib.sha1(header + data).hexdigest()


def read_bytes(path):
    with open(path, "rb") as handle:
        return handle.read()


def is_safe_relative_path(path):
    if not path or os.path.isabs(path) or "\\" in path:
        return False
    parts = path.split("/")
    return all(part not in ("", ".", "..") for part in parts)


def find_project_files(root):
    found = []
    for directory, subdirs, files in os.walk(root):
        subdirs[:] = sorted(d for d in subdirs if d not in SKIPPED_DIRS and not d.startswith("."))
        for name in sorted(files):
            if name.endswith(".csproj"):
                found.append(os.path.relpath(os.path.join(directory, name), root).replace(os.sep, "/"))
    return found


def project_items(project_path):
    """Return (protobuf_includes, package_versions) for one project file."""
    tree = ET.parse(project_path)
    includes = []
    versions = {}
    for element in tree.iter():
        tag = element.tag.split("}")[-1]
        if tag == "Protobuf" and element.get("Include"):
            includes.append(element.get("Include"))
        elif tag == "PackageReference" and element.get("Include"):
            versions[element.get("Include")] = element.get("Version")
    return includes, versions


def resolve_protobuf_include(include):
    """Map a <Protobuf Include> value to a repository-relative path, or None."""
    if include.startswith(API_PROTOS_ROOT_TOKEN):
        return VENDORED_PROTOS_DIR + include[len(API_PROTOS_ROOT_TOKEN):].replace("\\", "/")
    return None


def producer_url(repository, revision, path):
    match = GITHUB_REPOSITORY.match(repository)
    return "https://raw.githubusercontent.com/%s/%s/%s/%s" % (match.group(1), match.group(2), revision, path)


def check_source(index, source, root, errors):
    label = "sources[%d]" % index
    consumer_path = source.get("consumerPath")
    consumer_sha256 = source.get("consumerSha256")
    producer = source.get("producer") or {}
    generator = source.get("generator") or {}

    if not isinstance(consumer_path, str) or not is_safe_relative_path(consumer_path):
        errors.append("%s.consumerPath must be a safe repository-relative path: %r" % (label, consumer_path))
        return None
    if not consumer_path.startswith(VENDORED_PROTOS_DIR + "/"):
        errors.append("%s.consumerPath must live under %s/: %s" % (label, VENDORED_PROTOS_DIR, consumer_path))
    if not isinstance(consumer_sha256, str) or not FULL_SHA256.match(consumer_sha256):
        errors.append("%s.consumerSha256 must be a lowercase 64-hex SHA-256 digest" % label)

    repository = producer.get("repository")
    revision = producer.get("revision")
    producer_path = producer.get("path")
    producer_blob = producer.get("gitBlobSha1")
    producer_sha256 = producer.get("sha256")
    if not isinstance(repository, str) or not GITHUB_REPOSITORY.match(repository):
        errors.append("%s.producer.repository must be an https://github.com/<owner>/<repo> URL: %r" % (label, repository))
    if not isinstance(revision, str) or not FULL_SHA1.match(revision):
        errors.append(
            "%s.producer.revision must be an immutable full 40-hex commit id, not a branch/tag/short id: %r"
            % (label, revision))
    if not isinstance(producer_path, str) or not is_safe_relative_path(producer_path):
        errors.append("%s.producer.path must be a safe repository-relative path: %r" % (label, producer_path))
    if not isinstance(producer_blob, str) or not FULL_SHA1.match(producer_blob):
        errors.append("%s.producer.gitBlobSha1 must be a lowercase 40-hex git blob id" % label)
    if not isinstance(producer_sha256, str) or not FULL_SHA256.match(producer_sha256):
        errors.append("%s.producer.sha256 must be a lowercase 64-hex SHA-256 digest" % label)

    for key in ("tool", "version", "runtime", "runtimeVersion"):
        if not isinstance(generator.get(key), str) or not generator.get(key):
            errors.append("%s.generator.%s is required" % (label, key))
    projects = generator.get("projects")
    if not isinstance(projects, list) or not projects:
        errors.append("%s.generator.projects must list the project files that compile this proto" % label)

    consumer_file = os.path.join(root, consumer_path)
    if not os.path.isfile(consumer_file):
        errors.append("%s: consumer file is missing: %s" % (label, consumer_path))
        return None

    data = read_bytes(consumer_file)
    actual_sha256 = sha256_hex(data)
    actual_blob = git_blob_sha1_hex(data)
    if actual_sha256 != consumer_sha256:
        errors.append(
            "%s: %s SHA-256 is %s but the manifest declares consumerSha256 %s "
            "(vendored bytes changed without a manifest update)" % (label, consumer_path, actual_sha256, consumer_sha256))
    if consumer_sha256 != producer_sha256:
        errors.append(
            "%s: consumerSha256 %s differs from producer.sha256 %s; the vendored copy must be byte-identical"
            % (label, consumer_sha256, producer_sha256))
    if actual_blob != producer_blob:
        errors.append(
            "%s: %s git blob id is %s but producer %s@%s declares %s"
            % (label, consumer_path, actual_blob, producer_path, revision, producer_blob))
    return source


def check_projects(root, sources, errors):
    declared = {}
    for source in sources:
        declared[source["consumerPath"]] = source

    compiled_by = {}
    for project in find_project_files(root):
        try:
            includes, versions = project_items(os.path.join(root, project))
        except ET.ParseError as exc:
            errors.append("%s: cannot parse project file: %s" % (project, exc))
            continue
        for include in includes:
            resolved = resolve_protobuf_include(include)
            if resolved is None:
                errors.append(
                    "%s: <Protobuf Include=\"%s\"> must compile a vendored proto through %s"
                    % (project, include, API_PROTOS_ROOT_TOKEN))
                continue
            source = declared.get(resolved)
            if source is None:
                errors.append("%s: compiles %s, which is not declared in %s" % (project, resolved, MANIFEST_RELATIVE_PATH))
                continue
            compiled_by.setdefault(resolved, []).append(project)
            generator = source.get("generator") or {}
            for package, key in ((generator.get("tool"), "version"), (generator.get("runtime"), "runtimeVersion")):
                if package and versions.get(package) != generator.get(key):
                    errors.append(
                        "%s: %s version is %s but %s generator.%s declares %s"
                        % (project, package, versions.get(package), resolved, key, generator.get(key)))

    for path, source in sorted(declared.items()):
        listed = sorted((source.get("generator") or {}).get("projects") or [])
        actual = sorted(compiled_by.get(path, []))
        if listed != actual:
            errors.append("%s: generator.projects is %s but the projects compiling it are %s" % (path, listed, actual))


def check_source_doc(root, sources, errors):
    doc_path = os.path.join(root, SOURCE_DOC_RELATIVE_PATH)
    if not os.path.isfile(doc_path):
        errors.append("%s is missing" % SOURCE_DOC_RELATIVE_PATH)
        return
    text = read_bytes(doc_path).decode("utf-8")
    for source in sources:
        revision = (source.get("producer") or {}).get("revision")
        if revision and revision not in text:
            errors.append("%s does not mention the pinned producer revision %s" % (SOURCE_DOC_RELATIVE_PATH, revision))


def check_producer_bytes(source, data, origin, errors):
    producer = source["producer"]
    if sha256_hex(data) != producer["sha256"]:
        errors.append("%s: producer SHA-256 is %s, manifest declares %s" % (origin, sha256_hex(data), producer["sha256"]))
    if git_blob_sha1_hex(data) != producer["gitBlobSha1"]:
        errors.append(
            "%s: producer git blob id is %s, manifest declares %s" % (origin, git_blob_sha1_hex(data), producer["gitBlobSha1"]))


def fetch(url, timeout):
    response = urlopen(url, timeout=timeout)
    try:
        return response.read()
    finally:
        response.close()


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                        help="NodeKit repository root (default: parent of scripts/)")
    parser.add_argument("--fetch-producer", action="store_true",
                        help="also download each producer file at its pinned commit and verify its digests")
    parser.add_argument("--producer-dir",
                        help="also verify each producer file against a local checkout of the pinned commit")
    parser.add_argument("--print-producer-urls", action="store_true",
                        help="print the immutable producer URLs that --fetch-producer would use, then exit")
    parser.add_argument("--timeout", type=float, default=30.0)
    args = parser.parse_args(argv)

    root = os.path.abspath(args.root)
    manifest_path = os.path.join(root, MANIFEST_RELATIVE_PATH)
    errors = []
    try:
        manifest_text = read_bytes(manifest_path).decode("utf-8")
        manifest = json.loads(manifest_text)
    except (IOError, OSError, ValueError) as exc:
        print("FAIL: cannot read %s: %s" % (MANIFEST_RELATIVE_PATH, exc))
        return 1

    if not isinstance(manifest, dict):
        print("FAIL: %s must be a JSON object" % MANIFEST_RELATIVE_PATH)
        return 1
    if manifest.get("schemaVersion") != 1:
        errors.append("schemaVersion must be 1, got %r" % manifest.get("schemaVersion"))
    sources = manifest.get("sources")
    if not isinstance(sources, list) or not sources:
        errors.append("sources must be a non-empty list")
        sources = []

    checked = []
    seen = set()
    for index, source in enumerate(sources):
        if not isinstance(source, dict):
            errors.append("sources[%d] must be an object" % index)
            continue
        result = check_source(index, source, root, errors)
        if result is None:
            continue
        if result["consumerPath"] in seen:
            errors.append("sources[%d]: duplicate consumerPath %s" % (index, result["consumerPath"]))
        # Directory.Build.targets matches this exact text before Protobuf compilation.
        if ('"consumerSha256": "%s"' % result.get("consumerSha256")) not in manifest_text:
            errors.append(
                "sources[%d]: write consumerSha256 as '\"consumerSha256\": \"<hex>\"' "
                "so the MSBuild official-build guard can match it" % index)
        seen.add(result["consumerPath"])
        checked.append(result)

    if errors:
        for error in errors:
            print("FAIL: " + error)
        return 1

    if args.print_producer_urls:
        for source in checked:
            producer = source["producer"]
            print(producer_url(producer["repository"], producer["revision"], producer["path"]))
        return 0

    check_projects(root, checked, errors)
    check_source_doc(root, checked, errors)

    for source in checked:
        producer = source["producer"]
        if args.producer_dir:
            local = os.path.join(os.path.abspath(args.producer_dir), producer["path"])
            if not os.path.isfile(local):
                errors.append("producer file missing in --producer-dir: %s" % local)
            else:
                check_producer_bytes(source, read_bytes(local), local, errors)
        if args.fetch_producer:
            url = producer_url(producer["repository"], producer["revision"], producer["path"])
            try:
                data = fetch(url, args.timeout)
            except Exception as exc:  # network failure must fail closed
                errors.append("cannot fetch pinned producer %s: %s" % (url, exc))
            else:
                check_producer_bytes(source, data, url, errors)

    if errors:
        for error in errors:
            print("FAIL: " + error)
        return 1

    for source in checked:
        producer = source["producer"]
        print("OK: %s == %s %s@%s (sha256 %s, blob %s)" % (
            source["consumerPath"], producer["repository"], producer["path"], producer["revision"],
            source["consumerSha256"], producer["gitBlobSha1"]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
