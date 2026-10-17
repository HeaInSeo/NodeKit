#!/usr/bin/env python3
"""
Fixture tests for write-cli-manifest.py, run as a real subprocess against a
small synthetic publish directory (not a real dotnet publish) so this stays
fast and self-contained.

Run directly: python3 scripts/test_write_cli_manifest.py
"""

import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "write-cli-manifest.py")
TEMPLATE_PATH = os.path.join(SCRIPT_DIR, "..", "release", "cli-manifest.template.json")

DEPS_LIBRARIES = {
    "NodeKit.Cli/1.0.0": {"type": "project"},
    "Spectre.Console/0.57.1": {"type": "package"},
    "Grpc.Net.Client/2.76.0": {"type": "package"},
    "Google.Protobuf/3.34.1": {"type": "package"},
}


class WriteCliManifestTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.publish = os.path.join(self.tmp, "publish")
        os.makedirs(os.path.join(self.publish, "sub"))
        self.write_file("NodeKit.Cli", b"apphost", executable=True)
        self.write_file("NodeKit.Cli.dll", b"assembly")
        self.write_file("sub/data.bin", b"data")
        self.write_deps(DEPS_LIBRARIES)
        self.contract = os.path.join(self.tmp, "provenance.json")
        with open(self.contract, "wb") as f:
            f.write(b"{}\n")
        self.out = os.path.join(self.tmp, "cli-manifest.json")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def write_file(self, rel, content, executable=False):
        path = os.path.join(self.publish, rel)
        with open(path, "wb") as f:
            f.write(content)
        mode = 0o755 if executable else 0o644
        os.chmod(path, mode)

    def write_deps(self, libraries):
        with open(os.path.join(self.publish, "NodeKit.Cli.deps.json"), "w") as f:
            json.dump({"libraries": libraries}, f)

    def run_script(self, out=None):
        args = [
            sys.executable, SCRIPT_PATH,
            "--template", TEMPLATE_PATH,
            "--publish-dir", self.publish,
            "--version", "0.9.1",
            "--source-sha", "a" * 40,
            "--source-dirty", "false",
            "--contract-source", self.contract,
            "--dotnet-sdk", "10.0.201",
            "--out", out or self.out,
        ]
        return subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              universal_newlines=True)

    def load(self):
        with open(self.out) as f:
            return json.load(f)

    def test_writes_profile_source_and_closure(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        manifest = self.load()
        self.assertEqual(manifest["version"], "0.9.1")
        self.assertEqual(manifest["profile"]["rid"], "linux-x64")
        self.assertTrue(manifest["profile"]["self_contained"])
        self.assertEqual(manifest["source"], {"repository": "HeaInSeo/NodeKit", "sha": "a" * 40, "dirty": False})
        self.assertEqual(manifest["contract_source"]["sha256"],
                         "ca3d163bab055381827226140568f3bef7eaac187cebd76878e0b63e9e442356")
        self.assertEqual(manifest["build"]["dotnet_sdk"], "10.0.201")
        self.assertEqual([e["name"] for e in manifest["runtime_closure"]],
                         ["Google.Protobuf", "Grpc.Net.Client", "Spectre.Console"])

    def test_files_have_hash_size_and_executable_flag(self):
        self.assertEqual(self.run_script().returncode, 0)
        files = {e["path"]: e for e in self.load()["files"]}
        self.assertEqual(sorted(files), ["NodeKit.Cli", "NodeKit.Cli.deps.json", "NodeKit.Cli.dll", "sub/data.bin"])
        self.assertTrue(files["NodeKit.Cli"]["executable"])
        self.assertFalse(files["NodeKit.Cli.dll"]["executable"])
        self.assertEqual(files["sub/data.bin"]["size"], 4)
        self.assertEqual(files["sub/data.bin"]["sha256"],
                         "3a6eb0790f39ac87c94f3856b2dd2c5d110e6811602261a9a923d3bb23adc8b7")

    def test_manifest_inside_publish_dir_is_not_listed(self):
        inside = os.path.join(self.publish, "cli-manifest.json")
        self.assertEqual(self.run_script(out=inside).returncode, 0)
        self.assertEqual(self.run_script(out=inside).returncode, 0)
        with open(inside) as f:
            paths = [e["path"] for e in json.load(f)["files"]]
        self.assertNotIn("cli-manifest.json", paths)

    def test_rejects_gui_or_k8s_package_in_closure(self):
        for name in ("Avalonia/11.0.0", "SkiaSharp/2.88.0", "Wasmtime/22.0.0", "KubernetesClient/15.0.0"):
            libraries = dict(DEPS_LIBRARIES)
            libraries[name] = {"type": "package"}
            self.write_deps(libraries)
            result = self.run_script()
            self.assertEqual(result.returncode, 1, name)
            self.assertIn(name.split("/")[0], result.stderr)
            self.assertFalse(os.path.exists(self.out), name)

    def test_rejects_missing_or_non_executable_entrypoint(self):
        os.chmod(os.path.join(self.publish, "NodeKit.Cli"), stat.S_IRUSR | stat.S_IWUSR)
        result = self.run_script()
        self.assertEqual(result.returncode, 1)
        self.assertIn("not executable", result.stderr)
        os.remove(os.path.join(self.publish, "NodeKit.Cli"))
        result = self.run_script()
        self.assertEqual(result.returncode, 1)
        self.assertIn("missing", result.stderr)
        self.assertFalse(os.path.exists(self.out))

    def test_rejects_missing_deps_json(self):
        os.remove(os.path.join(self.publish, "NodeKit.Cli.deps.json"))
        result = self.run_script()
        self.assertEqual(result.returncode, 1)
        self.assertIn("NodeKit.Cli.deps.json", result.stderr)

    def test_usage_error_exits_2(self):
        result = subprocess.run([sys.executable, SCRIPT_PATH], stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, universal_newlines=True)
        self.assertEqual(result.returncode, 2)


if __name__ == "__main__":
    unittest.main()
