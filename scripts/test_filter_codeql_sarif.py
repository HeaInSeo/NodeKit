#!/usr/bin/env python3
"""
Fixture tests for filter-codeql-sarif.py, run as a real subprocess against
a throwaway git repository with a controlled mix of tracked/untracked
files, so the git-tracked-file safety check is exercised for real (not
mocked).

Run directly: python3 scripts/test_filter_codeql_sarif.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "filter-codeql-sarif.py")


def make_result(rule_id, uri):
    return {
        "ruleId": rule_id,
        "message": {"text": "fixture message"},
        "locations": [
            {
                "physicalLocation": {
                    "artifactLocation": {"uri": uri, "uriBaseId": "%SRCROOT%"},
                    "region": {"startLine": 1},
                }
            }
        ],
    }


def make_sarif(results):
    return {
        "version": "2.1.0",
        "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
        "runs": [
            {
                "tool": {"driver": {"name": "CodeQL", "rules": []}},
                "results": results,
            }
        ],
    }


def run_git(repo, *args):
    result = subprocess.run(
        ["git"] + list(args),
        cwd=repo,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        universal_newlines=True,
    )
    if result.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), result.stderr))
    return result.stdout


class FilterCodeqlSarifTests(unittest.TestCase):
    def setUp(self):
        self.repo = tempfile.mkdtemp(prefix="filter-sarif-test-")
        run_git(self.repo, "init", "-q")
        run_git(self.repo, "config", "user.email", "test@example.com")
        run_git(self.repo, "config", "user.name", "Test")

        # A real tracked source file, and a real (untracked) obj/ file - the
        # exact shape this repo actually has.
        os.makedirs(os.path.join(self.repo, "src"))
        with open(os.path.join(self.repo, "src", "Foo.cs"), "w") as f:
            f.write("class Foo {}\n")
        run_git(self.repo, "add", "src/Foo.cs")
        run_git(self.repo, "commit", "-q", "-m", "initial")

        os.makedirs(os.path.join(self.repo, "src", "obj", "Release"))
        with open(os.path.join(self.repo, "src", "obj", "Release", "Generated.cs"), "w") as f:
            f.write("class Generated {}\n")
        # Deliberately NOT added/committed - matches real obj/ being gitignored.

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)

    def run_filter(self, sarif):
        input_path = os.path.join(self.repo, "in.sarif")
        output_path = os.path.join(self.repo, "out.sarif")
        with open(input_path, "w") as f:
            json.dump(sarif, f)

        result = subprocess.run(
            [sys.executable, SCRIPT_PATH, input_path, output_path],
            cwd=self.repo,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            universal_newlines=True,
        )
        output_sarif = None
        if os.path.isfile(output_path):
            with open(output_path) as f:
                output_sarif = json.load(f)
        return result, output_sarif

    def test_removes_untracked_obj_result_keeps_tracked_source_result(self):
        sarif = make_sarif([
            make_result("cs/simplifiable-boolean-expression", "src/obj/Release/Generated.cs"),
            make_result("cs/catch-of-all-exceptions", "src/Foo.cs"),
        ])

        result, output = self.run_filter(sarif)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Alerts before filtering: 2", result.stdout)
        self.assertIn("Alerts after filtering: 1", result.stdout)
        self.assertIn("Removed: 1", result.stdout)
        self.assertIn("Source-controlled files removed: 0", result.stdout)

        remaining_uris = [
            r["locations"][0]["physicalLocation"]["artifactLocation"]["uri"]
            for r in output["runs"][0]["results"]
        ]
        self.assertEqual(remaining_uris, ["src/Foo.cs"])

    def test_tracked_obj_file_is_not_removed_and_fails_loudly(self):
        # A tracked file under obj/ shouldn't exist in practice (obj/ is
        # gitignored), but if it did, the script must refuse to remove it
        # rather than silently dropping a real alert.
        os.makedirs(os.path.join(self.repo, "obj"), exist_ok=True)
        with open(os.path.join(self.repo, "obj", "Tracked.cs"), "w") as f:
            f.write("class Tracked {}\n")
        run_git(self.repo, "add", "obj/Tracked.cs")
        run_git(self.repo, "commit", "-q", "-m", "accidentally tracked obj file")

        sarif = make_sarif([
            make_result("cs/simplifiable-boolean-expression", "obj/Tracked.cs"),
        ])

        result, _ = self.run_filter(sarif)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SAFETY VIOLATION", result.stderr)
        self.assertIn("obj/Tracked.cs", result.stderr)

    def test_untracked_non_obj_path_is_not_removed(self):
        # bin/ (or anything else) is out of scope for this first pass -
        # only obj/ is targeted.
        sarif = make_sarif([
            make_result("cs/simplifiable-boolean-expression", "bin/Release/Whatever.cs"),
        ])

        result, output = self.run_filter(sarif)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Removed: 0", result.stdout)
        self.assertEqual(len(output["runs"][0]["results"]), 1)

    def test_unparseable_sarif_fails(self):
        input_path = os.path.join(self.repo, "bad.sarif")
        output_path = os.path.join(self.repo, "out.sarif")
        with open(input_path, "w") as f:
            f.write("{ not valid json")

        result = subprocess.run(
            [sys.executable, SCRIPT_PATH, input_path, output_path],
            cwd=self.repo,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            universal_newlines=True,
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Could not parse", result.stderr)

    def test_missing_runs_key_fails(self):
        result, _ = self.run_filter({"version": "2.1.0"})

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("structurally invalid", result.stderr)

    def test_missing_input_file_fails(self):
        result = subprocess.run(
            [sys.executable, SCRIPT_PATH, os.path.join(self.repo, "does-not-exist.sarif"),
             os.path.join(self.repo, "out.sarif")],
            cwd=self.repo,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            universal_newlines=True,
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("not found", result.stderr)

    def test_multiple_obj_results_and_rules_are_all_removed_and_counted(self):
        sarif = make_sarif([
            make_result("cs/simplifiable-boolean-expression", "src/obj/Release/Generated.cs"),
            make_result("cs/simplifiable-boolean-expression", "src/obj/Release/Generated.cs"),
            make_result("cs/useless-gethashcode-call", "src/obj/Release/Generated.cs"),
            make_result("cs/catch-of-all-exceptions", "src/Foo.cs"),
        ])

        result, output = self.run_filter(sarif)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Removed: 3", result.stdout)
        self.assertIn("cs/simplifiable-boolean-expression", result.stdout)
        self.assertIn("cs/useless-gethashcode-call", result.stdout)
        self.assertEqual(len(output["runs"][0]["results"]), 1)


if __name__ == "__main__":
    unittest.main()
