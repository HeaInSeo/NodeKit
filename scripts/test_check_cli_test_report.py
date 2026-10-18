#!/usr/bin/env python3
"""
Fixture tests for check-cli-test-report.py: small xUnit.net v2 XML reports
with one changed outcome each, run through the checker as a real subprocess.

Run directly: python3 scripts/test_check_cli_test_report.py
"""

import os
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CHECKER = os.path.join(SCRIPT_DIR, "check-cli-test-report.py")

LIVE = "NodeKit.Cli.Tests.GrpcToolSpecClientIntegrationTests"


def test_xml(test_type, method, result):
    return ('<test name="%s.%s" type="%s" method="%s" time="0.01" result="%s" />'
            % (test_type, method, test_type, method, result))


class CheckCliTestReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.report = os.path.join(self.tmp, "cli-tests.xunit.xml")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def write(self, tests, errors=0):
        with open(self.report, "w", encoding="utf-8") as f:
            f.write('<?xml version="1.0" encoding="utf-8"?>\n<assemblies>'
                    '<assembly name="NodeKit.Cli.Tests.dll" total="%d" errors="%d">'
                    '<collection name="c">%s</collection></assembly></assemblies>\n'
                    % (len(tests), errors, "".join(tests)))

    def passing(self, count):
        return [test_xml("NodeKit.Cli.Tests.CliAppTests", "T%d" % i, "Pass") for i in range(count)]

    def run_checker(self, min_total=3, allow=(LIVE,)):
        args = [sys.executable, CHECKER, "--report", self.report, "--min-total", str(min_total)]
        for cls in allow:
            args += ["--allow-skip-class", cls]
        return subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)

    def test_all_pass_with_allowed_live_skip_passes(self):
        self.write(self.passing(3) + [test_xml(LIVE, "Live", "Skip")])
        result = self.run_checker()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("total=4 failed=0 errors=0 skipped=1", result.stdout)

    def test_missing_report_fails(self):
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("cannot read report", result.stderr)

    def test_report_without_assembly_fails(self):
        with open(self.report, "w", encoding="utf-8") as f:
            f.write("<assemblies />\n")
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("has no <assembly>", result.stderr)

    def test_too_few_tests_fails(self):
        self.write(self.passing(2))
        result = self.run_checker(min_total=3)
        self.assertEqual(result.returncode, 1)
        self.assertIn("only 2 tests ran, expected at least 3", result.stderr)

    def test_empty_run_fails(self):
        self.write([])
        result = self.run_checker(min_total=1)
        self.assertEqual(result.returncode, 1)
        self.assertIn("only 0 tests ran", result.stderr)

    def test_failed_test_fails(self):
        self.write(self.passing(3) + [test_xml("NodeKit.Cli.Tests.CliAppTests", "Bad", "Fail")])
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("1 tests failed", result.stderr)

    def test_assembly_error_fails(self):
        self.write(self.passing(3), errors=1)
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("assembly/test errors", result.stderr)

    def test_unknown_result_fails(self):
        self.write(self.passing(3) + [test_xml("NodeKit.Cli.Tests.CliAppTests", "Odd", "NotRun")])
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("unknown results", result.stderr)

    def test_skip_outside_allowed_classes_fails(self):
        self.write(self.passing(3) + [test_xml("NodeKit.Cli.Tests.SubmitCommandTests", "Skipped", "Skip")])
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("skipped outside the allowed classes: NodeKit.Cli.Tests.SubmitCommandTests.Skipped",
                      result.stderr)

    def test_live_skip_without_allowlist_fails(self):
        self.write(self.passing(3) + [test_xml(LIVE, "Live", "Skip")])
        result = self.run_checker(allow=())
        self.assertEqual(result.returncode, 1)
        self.assertIn("skipped outside the allowed classes", result.stderr)

    def test_usage_error_exits_2(self):
        result = subprocess.run([sys.executable, CHECKER], stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, universal_newlines=True)
        self.assertEqual(result.returncode, 2)


if __name__ == "__main__":
    unittest.main()
