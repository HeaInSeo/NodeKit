#!/usr/bin/env python3
"""
Fixture tests for check-cli-closure.py, run as a real subprocess against small
synthetic lock/deps files (not a real dotnet build).

Run directly: python3 scripts/test_check_cli_closure.py
"""

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "check-cli-closure.py")
MANIFEST_WRITER_PATH = os.path.join(SCRIPT_DIR, "write-cli-manifest.py")


def lock_doc(extra_main=None, extra_rid=None):
    main = {
        "Google.Protobuf": {"type": "Direct"},
        "Grpc.Net.Client": {"type": "Direct"},
        "Grpc.Tools": {"type": "Direct"},
        "Spectre.Console": {"type": "Direct"},
        "Grpc.Core.Api": {"type": "Transitive"},
    }
    rid = {"Grpc.Core.Api": {"type": "Transitive"}}
    main.update(extra_main or {})
    rid.update(extra_rid or {})
    return {"version": 1, "dependencies": {"net10.0": main, "net10.0/linux-x64": rid}}


def deps_doc(extra=None, drop=()):
    libraries = {
        "NodeKit.Cli/1.0.0": {"type": "project"},
        "Spectre.Console/0.57.1": {"type": "package"},
        "Grpc.Net.Client/2.76.0": {"type": "package"},
        "Google.Protobuf/3.34.1": {"type": "package"},
        "Grpc.Core.Api/2.76.0": {"type": "package"},
    }
    libraries.update(extra or {})
    for key in drop:
        del libraries[key]
    return {"libraries": libraries}


def load_module(path, name):
    # Importing the scripts must not leave scripts/__pycache__ in the work tree.
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CheckCliClosureTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def run_script(self, lock, deps):
        lock_path = os.path.join(self.tmp, "packages.lock.json")
        deps_path = os.path.join(self.tmp, "NodeKit.Cli.deps.json")
        with open(lock_path, "w") as f:
            json.dump(lock, f)
        with open(deps_path, "w") as f:
            json.dump(deps, f)
        return subprocess.run(
            [sys.executable, SCRIPT_PATH, "--lock", lock_path, "--deps", deps_path],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)

    def assert_rejected(self, result, needle):
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn(needle, result.stderr)

    def test_clean_closure_passes_and_prints_runtime_packages(self):
        result = self.run_script(lock_doc(), deps_doc())
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = result.stdout.splitlines()
        self.assertIn("Spectre.Console 0.57.1", lines)
        self.assertIn("Grpc.Net.Client 2.76.0", lines)
        self.assertIn("Google.Protobuf 3.34.1", lines)
        self.assertNotIn("NodeKit.Cli 1.0.0", lines)

    def test_forbidden_transitive_in_rid_section_is_rejected(self):
        lock = lock_doc(extra_rid={"SkiaSharp.NativeAssets.Linux": {"type": "Transitive"}})
        self.assert_rejected(self.run_script(lock, deps_doc()),
                             "lock net10.0/linux-x64 contains GUI/K8s package SkiaSharp.NativeAssets.Linux")

    def test_forbidden_package_in_lock_is_rejected_for_each_prefix(self):
        for name in ("Avalonia", "Wasmtime", "KubernetesClient"):
            with self.subTest(name=name):
                lock = lock_doc(extra_main={name: {"type": "Transitive"}})
                self.assert_rejected(self.run_script(lock, deps_doc()),
                                     "lock net10.0 contains GUI/K8s package " + name)

    def test_forbidden_package_in_runtime_closure_is_rejected(self):
        deps = deps_doc(extra={"KubernetesClient/18.0.5": {"type": "package"}})
        self.assert_rejected(self.run_script(lock_doc(), deps),
                             "runtime closure contains GUI/K8s package KubernetesClient")

    def test_project_dependency_is_rejected(self):
        lock = lock_doc(extra_main={"nodekit": {"type": "Project"}})
        self.assert_rejected(self.run_script(lock, deps_doc()),
                             "lock net10.0 has project dependency nodekit")

    def test_required_package_not_direct_is_rejected(self):
        lock = lock_doc(extra_main={"Spectre.Console": {"type": "Transitive"}})
        self.assert_rejected(self.run_script(lock, deps_doc()),
                             "Spectre.Console is not a Direct dependency")

    def test_required_package_missing_from_runtime_closure_is_rejected(self):
        deps = deps_doc(drop=("Google.Protobuf/3.34.1",))
        self.assert_rejected(self.run_script(lock_doc(), deps),
                             "Google.Protobuf is missing from the runtime closure")

    def test_forbidden_list_matches_manifest_writer(self):
        checker = load_module(SCRIPT_PATH, "check_cli_closure")
        writer = load_module(MANIFEST_WRITER_PATH, "write_cli_manifest")
        self.assertEqual(checker.FORBIDDEN_PREFIXES, writer.FORBIDDEN_PREFIXES)


if __name__ == "__main__":
    unittest.main()
