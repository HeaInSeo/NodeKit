#!/usr/bin/env python3
"""
Fixture tests for ci-check-coverage.py, run as a real subprocess against
small synthetic Cobertura XML files (not the actual dotnet build output) so
this stays fast and self-contained.

Run directly: python3 scripts/test_ci_check_coverage.py
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "ci-check-coverage.py")

COBERTURA_TEMPLATE = """<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="%(overall_line_rate)s" branch-rate="%(overall_branch_rate)s"
          lines-covered="%(lines_covered)d" lines-valid="%(lines_valid)d"
          branches-covered="%(branches_covered)d" branches-valid="%(branches_valid)d">
  <packages>
    <package name="Fixture">
      <classes>
%(classes)s
      </classes>
    </package>
  </packages>
</coverage>
"""

CLASS_TEMPLATE = """
        <class name="%(name)s" filename="%(name)s.cs" line-rate="1" branch-rate="1">
          <lines>
%(lines)s
          </lines>
        </class>
"""


def make_line(number, hits, branch=False, condition_coverage=None):
    branch_attr = ' branch="True"' if branch else ' branch="False"'
    cc_attr = ' condition-coverage="%s"' % condition_coverage if condition_coverage else ""
    return '            <line number="%d" hits="%d"%s%s />' % (number, hits, branch_attr, cc_attr)


def make_class(name, covered_lines, total_lines, branch_covered=0, branch_total=0):
    # Simple deterministic fixture: first `covered_lines` of `total_lines`
    # are hit; one extra "branch" line encodes the requested branch ratio
    # via condition-coverage, matching what this toolchain actually emits.
    lines = []
    for i in range(1, total_lines + 1):
        hits = 1 if i <= covered_lines else 0
        lines.append(make_line(i, hits))
    if branch_total > 0:
        cc = "%d%% (%d/%d)" % (int(100 * branch_covered / branch_total), branch_covered, branch_total)
        lines.append(make_line(total_lines + 1, 1, branch=True, condition_coverage=cc))
    return CLASS_TEMPLATE % {"name": name, "lines": "\n".join(lines)}


def make_report(path, class_specs, overall_lines=(50, 100), overall_branches=(10, 20)):
    lines_covered, lines_valid = overall_lines
    branches_covered, branches_valid = overall_branches
    classes_xml = "\n".join(make_class(**spec) for spec in class_specs)
    content = COBERTURA_TEMPLATE % {
        "overall_line_rate": lines_covered / lines_valid if lines_valid else 1,
        "overall_branch_rate": branches_covered / branches_valid if branches_valid else 1,
        "lines_covered": lines_covered,
        "lines_valid": lines_valid,
        "branches_covered": branches_covered,
        "branches_valid": branches_valid,
        "classes": classes_xml,
    }
    with open(path, "w") as f:
        f.write(content)
    return path


ALL_CORE_CLASSES = (
    "NodeKit.Validation.Recipes.RecipeValidationPipeline",
    "NodeKit.Authoring.Recipes.RecipeRenderer",
    "NodeKit.Cli.SubmitCommand",
    "NodeKit.Grpc.GrpcToolSpecClient",
    "NodeKit.Cli.HarborImageDigestResolver",
)


def well_covered_specs():
    return [
        {"name": name, "covered_lines": 90, "total_lines": 100, "branch_covered": 9, "branch_total": 10}
        for name in ALL_CORE_CLASSES
    ]


class CiCheckCoverageTests(unittest.TestCase):
    def setUp(self):
        self.tmpdir = tempfile.mkdtemp(prefix="ci-check-coverage-test-")

    def tearDown(self):
        shutil.rmtree(self.tmpdir, ignore_errors=True)

    def run_script(self, report_paths, env_overrides=None):
        env = dict(os.environ)
        env.pop("MINIMUM_LINE_RATE", None)
        env.pop("MINIMUM_BRANCH_RATE", None)
        env.pop("CORE_MINIMUM_LINE_RATE", None)
        env.pop("CORE_MINIMUM_BRANCH_RATE", None)
        if env_overrides:
            env.update(env_overrides)
        return subprocess.run(
            [sys.executable, SCRIPT_PATH] + list(report_paths),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            universal_newlines=True,
            env=env,
        )

    def test_core_classes_split_across_two_reports_all_found(self):
        # SubmitCommand only in report B, everything else only in report A —
        # mirrors the real NodeKit.Tests / NodeKit.Cli.Tests split.
        specs_a = [s for s in well_covered_specs() if s["name"] != "NodeKit.Cli.SubmitCommand"]
        specs_b = [s for s in well_covered_specs() if s["name"] == "NodeKit.Cli.SubmitCommand"]
        report_a = make_report(os.path.join(self.tmpdir, "a.xml"), specs_a)
        report_b = make_report(os.path.join(self.tmpdir, "b.xml"), specs_b)

        result = self.run_script([report_a, report_b])

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("NodeKit.Cli.SubmitCommand", result.stdout)

    def test_genuinely_missing_core_class_fails(self):
        specs = [s for s in well_covered_specs() if s["name"] != "NodeKit.Cli.SubmitCommand"]
        report = make_report(os.path.join(self.tmpdir, "a.xml"), specs)

        result = self.run_script([report])

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Core class not found in any coverage report: NodeKit.Cli.SubmitCommand", result.stderr)

    def test_stale_namespace_is_not_what_the_script_looks_for(self):
        # A report using the pre-relocation "NodeKit.Cli.GrpcToolSpecClient"
        # name (instead of the real current "NodeKit.Grpc.GrpcToolSpecClient")
        # must NOT satisfy the gate — guards against re-introducing the
        # stale-namespace bug this fix was for.
        specs = [
            s if s["name"] != "NodeKit.Grpc.GrpcToolSpecClient"
            else dict(s, name="NodeKit.Cli.GrpcToolSpecClient")
            for s in well_covered_specs()
        ]
        report = make_report(os.path.join(self.tmpdir, "a.xml"), specs)

        result = self.run_script([report])

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Core class not found in any coverage report: NodeKit.Grpc.GrpcToolSpecClient", result.stderr)

    def test_core_class_below_threshold_fails(self):
        specs = [s for s in well_covered_specs() if s["name"] != "NodeKit.Cli.SubmitCommand"]
        specs.append({
            "name": "NodeKit.Cli.SubmitCommand",
            "covered_lines": 10, "total_lines": 100,
            "branch_covered": 1, "branch_total": 10,
        })
        report = make_report(os.path.join(self.tmpdir, "a.xml"), specs)

        result = self.run_script([report])

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SubmitCommand line coverage", result.stderr)

    def test_overall_threshold_not_met_fails(self):
        report = make_report(
            os.path.join(self.tmpdir, "a.xml"),
            well_covered_specs(),
            overall_lines=(1, 100),
            overall_branches=(1, 100),
        )

        result = self.run_script([report], env_overrides={"MINIMUM_LINE_RATE": "0.50"})

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Line coverage is below threshold.", result.stderr)

    def test_no_reports_found_fails(self):
        result = self.run_script([os.path.join(self.tmpdir, "does-not-exist.xml")])

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("coverage report not found", result.stderr)

    def test_no_reports_and_no_explicit_paths_fails(self):
        cwd = os.getcwd()
        os.chdir(self.tmpdir)
        try:
            result = subprocess.run(
                [sys.executable, SCRIPT_PATH], stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True
            )
        finally:
            os.chdir(cwd)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("No coverage.cobertura.xml files found.", result.stderr)

    def test_class_shared_across_assemblies_takes_best_rate_not_sum(self):
        # GrpcToolSpecClient.cs is compiled into two assemblies; report A's
        # copy is barely touched (this project doesn't exercise it), report
        # B's copy is well covered. The combined result must reflect B's
        # rate, not a diluted average that could fail the gate even though
        # the code genuinely is well covered somewhere.
        specs_a = [s for s in well_covered_specs() if s["name"] != "NodeKit.Grpc.GrpcToolSpecClient"]
        specs_a.append({
            "name": "NodeKit.Grpc.GrpcToolSpecClient",
            "covered_lines": 0, "total_lines": 100,
            "branch_covered": 0, "branch_total": 10,
        })
        specs_b = [{
            "name": "NodeKit.Grpc.GrpcToolSpecClient",
            "covered_lines": 90, "total_lines": 100,
            "branch_covered": 9, "branch_total": 10,
        }]
        report_a = make_report(os.path.join(self.tmpdir, "a.xml"), specs_a)
        report_b = make_report(os.path.join(self.tmpdir, "b.xml"), specs_b)

        result = self.run_script([report_a, report_b])

        self.assertEqual(result.returncode, 0, result.stderr)
        # Report B's own rate is ~0.90; summing raw counts across both
        # compiled copies would dilute this to ~0.45 (90 covered / 201
        # valid) and fail the 0.70 core threshold. Assert it's close to
        # B's rate, not the diluted sum.
        match = re.search(
            r"NodeKit\.Grpc\.GrpcToolSpecClient \(line-rate: ([\d.]+), branch-rate: ([\d.]+)\)",
            result.stdout,
        )
        self.assertIsNotNone(match, result.stdout)
        self.assertGreater(float(match.group(1)), 0.85)
        self.assertGreater(float(match.group(2)), 0.85)


if __name__ == "__main__":
    unittest.main()
