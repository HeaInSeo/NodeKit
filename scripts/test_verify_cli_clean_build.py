#!/usr/bin/env python3
"""
Regression tests for verify-cli-clean-build.sh: every input it checks must come
from the archived HEAD checkout, never from the (possibly dirty) working tree.

Each test copies the script, the closure checker, the CLI lock file and the
vendored proto into a throwaway git repository, commits them, then makes an
uncommitted edit and runs the script with a stub `dotnet` on PATH (no real
build). The stub fails a build with NKPROTO004 when the checkout has no
vendored proto, otherwise writes the deps.json given in NK_STUB_DEPS.

Run directly: python3 scripts/test_verify_cli_clean_build.py
"""

import json
import os
import shutil
import stat
import subprocess
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
CHECKER_REL = "scripts/check-cli-closure.py"
VERIFY_REL = "scripts/verify-cli-clean-build.sh"
LOCK_REL = "src/NodeKit.Cli/packages.lock.json"
PROTO_REL = "protos/nodevault/v1/nodevault.proto"

STUB_DOTNET = r"""#!/usr/bin/env bash
set -euo pipefail
[ "$1" = "build" ] || exit 0
proj="$2"
config=Debug
prev=""
for arg in "$@"; do
    [ "$prev" = "-c" ] && config="$arg"
    prev="$arg"
done
root="$(cd "$(dirname "$proj")/../.." && pwd)"
if [ ! -f "$root/protos/nodevault/v1/nodevault.proto" ]; then
    echo "$proj : error NKPROTO004: vendored proto missing [$proj]"
    exit 1
fi
out="$(dirname "$proj")/bin/$config/net10.0"
mkdir -p "$out"
cp "$NK_STUB_DEPS" "$out/NodeKit.Cli.deps.json"
"""


def deps_doc(extra=None):
    libraries = {
        "NodeKit.Cli/1.0.0": {"type": "project"},
        "Spectre.Console/0.57.1": {"type": "package"},
        "Grpc.Net.Client/2.76.0": {"type": "package"},
        "Google.Protobuf/3.34.1": {"type": "package"},
    }
    libraries.update(extra or {})
    return {"libraries": libraries}


class VerifyCliCleanBuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.repo = os.path.join(self.tmp, "repo")
        for rel in (VERIFY_REL, CHECKER_REL, LOCK_REL, PROTO_REL):
            dst = os.path.join(self.repo, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(os.path.join(REPO_ROOT, rel), dst)
        self.git("init", "-q")
        self.git("add", "-A")
        self.git("-c", "user.name=t", "-c", "user.email=t@example.invalid",
                 "commit", "-q", "-m", "fixture")
        bin_dir = os.path.join(self.tmp, "bin")
        os.makedirs(bin_dir)
        stub = os.path.join(bin_dir, "dotnet")
        with open(stub, "w") as f:
            f.write(STUB_DOTNET)
        os.chmod(stub, os.stat(stub).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
        self.env = dict(os.environ)
        self.env["PATH"] = bin_dir + os.pathsep + self.env.get("PATH", "")
        self.set_stub_deps(deps_doc())

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def git(self, *args):
        subprocess.run(["git", "-C", self.repo] + list(args), check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def set_stub_deps(self, doc):
        path = os.path.join(self.tmp, "stub.deps.json")
        with open(path, "w") as f:
            json.dump(doc, f)
        self.env["NK_STUB_DEPS"] = path

    def dirty_checker(self, exit_code):
        # Uncommitted replacement of the checker: it ignores its input.
        with open(os.path.join(self.repo, CHECKER_REL), "w") as f:
            f.write("import sys\nprint('DIRTY CHECKER')\nsys.exit(%d)\n" % exit_code)

    def run_verify(self):
        return subprocess.run(
            ["bash", os.path.join(self.repo, VERIFY_REL)], env=self.env,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, universal_newlines=True)

    def test_clean_tree_passes(self):
        result = self.run_verify()
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("verify-cli-clean-build: PASS", result.stdout)

    def test_dirty_failing_checker_is_ignored(self):
        self.dirty_checker(1)
        result = self.run_verify()
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertNotIn("DIRTY CHECKER", result.stdout)

    def test_dirty_passing_checker_cannot_hide_forbidden_package(self):
        self.set_stub_deps(deps_doc(extra={"KubernetesClient/18.0.5": {"type": "package"}}))
        self.dirty_checker(0)
        result = self.run_verify()
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertNotIn("DIRTY CHECKER", result.stdout)
        self.assertIn("runtime closure contains GUI/K8s package KubernetesClient", result.stdout)

    def test_uncommitted_proto_deletion_is_ignored(self):
        os.remove(os.path.join(self.repo, PROTO_REL))
        result = self.run_verify()
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("verify-cli-clean-build: PASS", result.stdout)


if __name__ == "__main__":
    unittest.main()
