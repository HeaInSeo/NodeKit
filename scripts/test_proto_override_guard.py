#!/usr/bin/env python3
"""
Tests for the NodeKitVerifyProtoProvenance MSBuild target
(Directory.Build.targets): an official build must refuse proto bytes that
protos/provenance.json does not declare, including bytes injected through
/p:ApiProtosRoot=<dir>.

Needs the dotnet SDK and a restored solution (CI runs it after Restore/Build).
Run directly: python3 scripts/test_proto_override_guard.py
"""

import os
import shutil
import subprocess
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
PROTO = os.path.join("nodevault", "v1", "nodevault.proto")
PROJECTS = (
    os.path.join(REPO_ROOT, "NodeKit.csproj"),
    os.path.join(REPO_ROOT, "src", "NodeKit.Cli", "NodeKit.Cli.csproj"),
)
GUARD_TARGET = "NodeKitVerifyProtoProvenance"


def run(args):
    # No MSBuild node/build-server reuse: lingering background nodes would keep
    # loading the machine after this script exits.
    env = dict(os.environ)
    env["MSBUILDDISABLENODEREUSE"] = "1"
    # Keep the decision under the test's control: the guard also treats the
    # CI environment variable as "official", which would hide the Debug case.
    env.pop("CI", None)
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    process = subprocess.Popen(
        ["dotnet"] + args, cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
    output = process.communicate()[0].decode("utf-8", "replace")
    return process.returncode, output


class ProtoOverrideGuardTests(unittest.TestCase):
    def setUp(self):
        self.override = tempfile.mkdtemp(prefix="nk-proto-override-")
        os.makedirs(os.path.join(self.override, "nodevault", "v1"))
        shutil.copyfile(os.path.join(REPO_ROOT, "protos", PROTO), os.path.join(self.override, PROTO))

    def tearDown(self):
        shutil.rmtree(self.override)

    def tamper_override(self):
        with open(os.path.join(self.override, PROTO), "ab") as handle:
            handle.write(b"\n// undeclared byte\n")

    def guard(self, project, *properties):
        return run(["msbuild", project, "-nologo", "-t:" + GUARD_TARGET] + ["-p:" + p for p in properties])

    def test_vendored_proto_passes_official_guard(self):
        for project in PROJECTS:
            code, output = self.guard(project, "ContinuousIntegrationBuild=true")
            self.assertEqual(code, 0, output)
            self.assertNotIn("NKPROTO", output)

    def test_t3_undeclared_override_fails_in_ci_build(self):
        self.tamper_override()
        for project in PROJECTS:
            code, output = self.guard(project, "ContinuousIntegrationBuild=true", "ApiProtosRoot=" + self.override)
            self.assertNotEqual(code, 0, output)
            self.assertIn("NKPROTO002", output)

    def test_t3_undeclared_override_fails_in_release_build(self):
        self.tamper_override()
        code, output = self.guard(PROJECTS[1], "Configuration=Release", "ApiProtosRoot=" + self.override)
        self.assertNotEqual(code, 0, output)
        self.assertIn("NKPROTO002", output)

    def test_t3_undeclared_override_fails_under_ci_environment(self):
        self.tamper_override()
        code, output = self.guard(PROJECTS[1], "CI=true", "ApiProtosRoot=" + self.override)
        self.assertNotEqual(code, 0, output)
        self.assertIn("NKPROTO002", output)

    def test_t3_override_with_declared_bytes_is_proven_and_passes(self):
        code, output = self.guard(PROJECTS[1], "ContinuousIntegrationBuild=true", "ApiProtosRoot=" + self.override)
        self.assertEqual(code, 0, output)

    def test_local_debug_override_is_not_blocked(self):
        self.tamper_override()
        code, output = self.guard(PROJECTS[1], "Configuration=Debug", "ApiProtosRoot=" + self.override)
        self.assertEqual(code, 0, output)
        self.assertNotIn("NKPROTO", output)

    def test_t3_guard_runs_before_proto_compilation_in_real_build(self):
        # A real Release build of the CLI must stop at the guard before
        # Grpc.Tools compiles the undeclared proto.
        self.tamper_override()
        code, output = run([
            "build", PROJECTS[1], "--no-restore", "--configuration", "Release", "-nologo",
            "--disable-build-servers", "-p:ApiProtosRoot=" + self.override])
        self.assertNotEqual(code, 0, output)
        self.assertIn("NKPROTO002", output)
        self.assertNotIn("error CS", output)


if __name__ == "__main__":
    unittest.main()
