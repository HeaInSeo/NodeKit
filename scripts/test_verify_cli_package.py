#!/usr/bin/env python3
"""
Fixture tests for verify-cli-package.py (S4-05-C). Each test builds a small
synthetic package the way scripts/package-cli.sh lays it out (publish dir,
manifest from the real write-cli-manifest.py, tar.gz, .sha256, sidecar), then
removes or changes one output and runs the checker as a real subprocess.

Run directly: python3 scripts/test_verify_cli_package.py
"""

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
VERIFY = os.path.join(SCRIPT_DIR, "verify-cli-package.py")
WRITER = os.path.join(SCRIPT_DIR, "write-cli-manifest.py")
TEMPLATE = os.path.join(SCRIPT_DIR, "..", "release", "cli-manifest.template.json")

VERSION = "0.0.0-ci"
NAME = "nodekit-cli-%s-linux-x64" % VERSION


class VerifyCliPackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.stage = os.path.join(self.tmp, "stage")
        self.publish = os.path.join(self.stage, NAME)
        self.out = os.path.join(self.tmp, "out")
        os.makedirs(os.path.join(self.publish, "sub"))
        os.makedirs(self.out)
        self.write_publish("NodeKit.Cli", b"apphost", executable=True)
        self.write_publish("NodeKit.Cli.dll", b"assembly")
        self.write_publish("sub/data.bin", b"data")
        self.write_publish("NodeKit.Cli.deps.json",
                           json.dumps({"libraries": {"Grpc.Net.Client/2.76.0": {"type": "package"}}}).encode())
        self.contract = os.path.join(self.tmp, "provenance.json")
        with open(self.contract, "wb") as f:
            f.write(b"{}\n")
        self.write_manifest()

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def write_publish(self, rel, content, executable=False):
        path = os.path.join(self.publish, rel)
        with open(path, "wb") as f:
            f.write(content)
        os.chmod(path, 0o755 if executable else 0o644)

    def write_manifest(self):
        subprocess.run(
            [sys.executable, WRITER, "--template", TEMPLATE, "--publish-dir", self.publish,
             "--version", VERSION, "--source-sha", "0" * 40, "--source-dirty", "false",
             "--contract-source", self.contract, "--dotnet-sdk", "10.0.0",
             "--out", os.path.join(self.publish, "cli-manifest.json")],
            check=True)

    def edit_manifest(self, change):
        path = os.path.join(self.publish, "cli-manifest.json")
        with open(path, "r", encoding="utf-8") as f:
            manifest = json.load(f)
        change(manifest)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2)
            f.write("\n")

    def pack(self, extra_top=None):
        archive = os.path.join(self.out, NAME + ".tar.gz")
        with tarfile.open(archive, "w:gz") as tar:
            tar.add(self.publish, arcname=NAME)
            if extra_top:
                tar.add(extra_top, arcname=os.path.basename(extra_top))
        with open(archive, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        with open(archive + ".sha256", "w", encoding="utf-8") as f:
            f.write("%s  %s.tar.gz\n" % (digest, NAME))
        shutil.copyfile(os.path.join(self.publish, "cli-manifest.json"),
                        os.path.join(self.out, NAME + ".cli-manifest.json"))

    def run_verify(self, version=VERSION):
        return subprocess.run([sys.executable, VERIFY, "--dir", self.out, "--version", version],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)

    def assert_fails(self, expected):
        result = self.run_verify()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(expected, result.stderr)

    def test_complete_package_passes(self):
        self.pack()
        result = self.run_verify()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("PASS", result.stdout)

    def test_missing_archive_fails(self):
        self.pack()
        os.remove(os.path.join(self.out, NAME + ".tar.gz"))
        self.assert_fails("missing package output: %s.tar.gz" % NAME)

    def test_missing_checksum_fails(self):
        self.pack()
        os.remove(os.path.join(self.out, NAME + ".tar.gz.sha256"))
        self.assert_fails("missing package output: %s.tar.gz.sha256" % NAME)

    def test_missing_sidecar_manifest_fails(self):
        self.pack()
        os.remove(os.path.join(self.out, NAME + ".cli-manifest.json"))
        self.assert_fails("missing package output: %s.cli-manifest.json" % NAME)

    def test_wrong_version_dir_fails(self):
        self.pack()
        result = self.run_verify(version="0.0.1-ci")
        self.assertEqual(result.returncode, 1)
        self.assertIn("missing package output", result.stderr)

    def test_checksum_mismatch_fails(self):
        self.pack()
        with open(os.path.join(self.out, NAME + ".tar.gz.sha256"), "w", encoding="utf-8") as f:
            f.write("%s  %s.tar.gz\n" % ("0" * 64, NAME))
        self.assert_fails("does not match the archive hash")

    def test_missing_entrypoint_fails(self):
        os.remove(os.path.join(self.publish, "NodeKit.Cli"))
        self.edit_manifest(lambda m: m.update(files=[e for e in m["files"] if e["path"] != "NodeKit.Cli"]))
        self.pack()
        self.assert_fails("entrypoint 'NodeKit.Cli' is missing from the archive")

    def test_non_executable_entrypoint_fails(self):
        os.chmod(os.path.join(self.publish, "NodeKit.Cli"), 0o644)
        self.pack()
        self.assert_fails("entrypoint 'NodeKit.Cli' is not executable")

    def test_listed_file_missing_from_archive_fails(self):
        os.remove(os.path.join(self.publish, "sub", "data.bin"))
        self.pack()
        self.assert_fails("manifest file 'sub/data.bin' is missing from the archive")

    def test_changed_file_bytes_fail(self):
        self.write_publish("NodeKit.Cli.dll", b"other assembly")
        self.pack()
        self.assert_fails("manifest file 'NodeKit.Cli.dll' size")

    def test_unlisted_archive_file_fails(self):
        self.write_publish("stray.txt", b"stray")
        self.pack()
        self.assert_fails("archive file 'stray.txt' is not listed in the manifest")

    def test_extra_top_level_entry_fails(self):
        extra = os.path.join(self.tmp, "other")
        with open(extra, "wb") as f:
            f.write(b"x")
        self.pack(extra_top=extra)
        self.assert_fails("archive top-level entries")

    def test_sidecar_differs_from_archive_manifest_fails(self):
        self.pack()
        with open(os.path.join(self.out, NAME + ".cli-manifest.json"), "a", encoding="utf-8") as f:
            f.write(" ")
        self.assert_fails("differs from the sidecar manifest")

    def test_dirty_or_wrong_version_manifest_fails(self):
        def change(m):
            m["source"]["dirty"] = True
            m["version"] = "9.9.9"
        self.edit_manifest(change)
        self.pack()
        result = self.run_verify()
        self.assertEqual(result.returncode, 1)
        self.assertIn("manifest source.dirty is not false", result.stderr)
        self.assertIn("manifest version '9.9.9'", result.stderr)

    def test_usage_error_exits_2(self):
        result = subprocess.run([sys.executable, VERIFY], stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, universal_newlines=True)
        self.assertEqual(result.returncode, 2)


if __name__ == "__main__":
    unittest.main()
