#!/usr/bin/env python3
"""
Fixture tests for verify-proto-provenance.py, run as a real subprocess
against a temporary copy of the files it reads (protos/, the two project
files that compile the proto) so they stay fast and need no network.

Run directly: python3 scripts/test_verify_proto_provenance.py
"""

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "verify-proto-provenance.py")
PROTO = "protos/nodevault/v1/nodevault.proto"
FIXTURE_FILES = (
    "protos/provenance.json",
    "protos/SOURCE.md",
    PROTO,
    "NodeKit.csproj",
    "src/NodeKit.Cli/NodeKit.Cli.csproj",
)
# Any outbound HTTP(S) request in a test goes to this closed port and fails.
OFFLINE_ENV_KEYS = ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY")
DEAD_PROXY = "http://127.0.0.1:9"


class VerifyProtoProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="nk-proto-provenance-")
        for relative in FIXTURE_FILES:
            target = os.path.join(self.root, relative)
            if not os.path.isdir(os.path.dirname(target)):
                os.makedirs(os.path.dirname(target))
            shutil.copyfile(os.path.join(REPO_ROOT, relative), target)

    def tearDown(self):
        shutil.rmtree(self.root)

    def path(self, relative):
        return os.path.join(self.root, relative)

    def run_script(self, *extra):
        env = dict(os.environ)
        for key in OFFLINE_ENV_KEYS:
            env[key] = DEAD_PROXY
        env.pop("no_proxy", None)
        env.pop("NO_PROXY", None)
        process = subprocess.Popen(
            [sys.executable, SCRIPT_PATH, "--root", self.root, "--timeout", "5"] + list(extra),
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        output = process.communicate()[0].decode("utf-8", "replace")
        return process.returncode, output

    def load_manifest(self):
        with open(self.path("protos/provenance.json")) as handle:
            return json.load(handle)

    def write_manifest(self, manifest):
        with open(self.path("protos/provenance.json"), "w") as handle:
            json.dump(manifest, handle, indent=2)
            handle.write("\n")

    def flip_one_byte(self, relative):
        with open(self.path(relative), "rb") as handle:
            data = bytearray(handle.read())
        index = len(data) // 2
        data[index] = ord("X") if data[index] != ord("X") else ord("Y")
        with open(self.path(relative), "wb") as handle:
            handle.write(bytes(data))

    def replace_in(self, relative, old, new):
        with open(self.path(relative)) as handle:
            text = handle.read()
        self.assertIn(old, text)
        with open(self.path(relative), "w") as handle:
            handle.write(text.replace(old, new))

    def producer_dir_with(self, data):
        producer = tempfile.mkdtemp(prefix="nk-proto-producer-")
        self.addCleanup(shutil.rmtree, producer)
        target = os.path.join(producer, PROTO)
        os.makedirs(os.path.dirname(target))
        with open(target, "wb") as handle:
            handle.write(data)
        return producer

    def assertFails(self, expected, *extra):
        code, output = self.run_script(*extra)
        self.assertEqual(code, 1, output)
        self.assertIn(expected, output)

    # --- positive -------------------------------------------------------

    def test_clean_checkout_verifies_offline(self):
        code, output = self.run_script()
        self.assertEqual(code, 0, output)
        self.assertIn("OK: " + PROTO, output)

    def test_matching_producer_checkout_passes(self):
        with open(self.path(PROTO), "rb") as handle:
            producer = self.producer_dir_with(handle.read())
        code, output = self.run_script("--producer-dir", producer)
        self.assertEqual(code, 0, output)

    # --- T1: consumer tamper -------------------------------------------

    def test_t1_one_byte_consumer_tamper_without_manifest_update_fails(self):
        self.flip_one_byte(PROTO)
        self.assertFails("vendored bytes changed without a manifest update")

    def test_t1_consumer_and_consumer_digest_updated_still_fails_against_producer_blob(self):
        self.flip_one_byte(PROTO)
        with open(self.path(PROTO), "rb") as handle:
            digest = hashlib.sha256(handle.read()).hexdigest()
        manifest = self.load_manifest()
        manifest["sources"][0]["consumerSha256"] = digest
        self.write_manifest(manifest)
        self.assertFails("must be byte-identical")

    # --- T2: manifest tamper -------------------------------------------

    def test_t2_branch_name_revision_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["producer"]["revision"] = "main"
        self.write_manifest(manifest)
        self.assertFails("immutable full 40-hex commit id")

    def test_t2_short_revision_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["producer"]["revision"] = manifest["sources"][0]["producer"]["revision"][:12]
        self.write_manifest(manifest)
        self.assertFails("immutable full 40-hex commit id")

    def test_t2_revision_tamper_disagrees_with_source_doc(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["producer"]["revision"] = "0" * 40
        self.write_manifest(manifest)
        self.assertFails("does not mention the pinned producer revision")

    def test_t2_revision_tamper_detected_by_producer_bytes(self):
        manifest = self.load_manifest()
        old = manifest["sources"][0]["producer"]["revision"]
        new = "1" * 40
        manifest["sources"][0]["producer"]["revision"] = new
        self.write_manifest(manifest)
        self.replace_in("protos/SOURCE.md", old, new)
        producer = self.producer_dir_with(b"syntax = \"proto3\"; // bytes at another commit\n")
        self.assertFails("producer SHA-256 is", "--producer-dir", producer)

    def test_t2_consumer_digest_tamper_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["consumerSha256"] = "0" * 64
        self.write_manifest(manifest)
        self.assertFails("vendored bytes changed without a manifest update")

    def test_t2_producer_blob_tamper_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["producer"]["gitBlobSha1"] = "0" * 40
        self.write_manifest(manifest)
        self.assertFails("git blob id is")

    def test_t2_producer_digest_tamper_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["producer"]["sha256"] = "0" * 64
        self.write_manifest(manifest)
        self.assertFails("must be byte-identical")

    def test_missing_manifest_fails(self):
        os.remove(self.path("protos/provenance.json"))
        self.assertFails("cannot read")

    # --- undeclared build inputs ---------------------------------------

    def test_undeclared_protobuf_include_fails(self):
        self.replace_in(
            "src/NodeKit.Cli/NodeKit.Cli.csproj",
            "$(ApiProtosRoot)/nodevault/v1/nodevault.proto",
            "$(ApiProtosRoot)/nodevault/v2/nodevault.proto")
        self.assertFails("which is not declared in")

    def test_protobuf_include_outside_api_protos_root_fails(self):
        self.replace_in(
            "NodeKit.csproj",
            "$(ApiProtosRoot)/nodevault/v1/nodevault.proto",
            "/elsewhere/nodevault/v1/nodevault.proto")
        self.assertFails("must compile a vendored proto through")

    def test_generator_version_drift_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["generator"]["version"] = "0.0.1"
        self.write_manifest(manifest)
        self.assertFails("Grpc.Tools version is")

    def test_generator_project_list_drift_fails(self):
        manifest = self.load_manifest()
        manifest["sources"][0]["generator"]["projects"] = ["NodeKit.csproj"]
        self.write_manifest(manifest)
        self.assertFails("generator.projects is")

    def test_manifest_reformat_that_breaks_msbuild_guard_fails(self):
        manifest = self.load_manifest()
        with open(self.path("protos/provenance.json"), "w") as handle:
            json.dump(manifest, handle, separators=(",", ":"))
        self.assertFails("MSBuild official-build guard")

    # --- T4: deterministic, never the moving producer branch -----------

    def test_t4_required_check_needs_no_network(self):
        # Every HTTP(S) request goes to a dead proxy in run_script, so a
        # passing offline verdict proves the producer's moving main was not
        # consulted.
        code, output = self.run_script()
        self.assertEqual(code, 0, output)

    def test_t4_producer_url_is_pinned_to_the_declared_commit(self):
        revision = self.load_manifest()["sources"][0]["producer"]["revision"]
        code, output = self.run_script("--print-producer-urls")
        self.assertEqual(code, 0, output)
        self.assertEqual(
            output.strip(),
            "https://raw.githubusercontent.com/HeaInSeo/NodeVault/%s/%s" % (revision, PROTO))
        for moving in ("/main/", "/master/", "/HEAD/", "refs/heads"):
            self.assertNotIn(moving, output)

    def test_t4_moved_producer_main_does_not_affect_pinned_verdict(self):
        # A local producer tree that has moved on (different bytes) is only
        # consulted when explicitly passed; the default verdict is unchanged.
        moved = self.producer_dir_with(b"syntax = \"proto3\"; // producer main moved\n")
        self.assertEqual(self.run_script()[0], 0)
        self.assertFails("producer SHA-256 is", "--producer-dir", moved)

    def test_fetch_failure_fails_closed(self):
        self.assertFails("cannot fetch pinned producer", "--fetch-producer")


if __name__ == "__main__":
    unittest.main()
