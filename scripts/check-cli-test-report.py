#!/usr/bin/env python3
"""
S4-05-A/C: reads the xUnit.net (v2+ XML) report of the NodeKit.Cli.Tests run
and fails when the run cannot count as a pass:
- the report is missing or has no <assembly>
- fewer than --min-total tests ran (a filter, a lost test class or an empty
  run must not pass)
- any test failed or errored
- a test was skipped outside --allow-skip-class. Only the live NodeVault
  integration classes may skip, because CI runs with no NODEKIT_NODEVAULT_URL;
  other skips are not a pass.

Exit codes: 0 PASS, 1 FAIL, 2 usage error.
"""

import argparse
import sys
import xml.etree.ElementTree as ET


def check(report, min_total, allowed_classes):
    """Returns (errors, summary)."""
    try:
        root = ET.parse(report).getroot()
    except (OSError, ET.ParseError) as e:
        return ["cannot read report %s: %s" % (report, e)], None

    assemblies = root.findall("assembly") if root.tag == "assemblies" else [root] if root.tag == "assembly" else []
    if not assemblies:
        return ["report %s has no <assembly>" % report], None

    total = failed = errored = 0
    skipped = []
    for assembly in assemblies:
        errored += int(assembly.get("errors", "0"))
        for test in assembly.iter("test"):
            total += 1
            result = test.get("result")
            if result == "Fail":
                failed += 1
            elif result == "Skip":
                skipped.append((test.get("type", ""), test.get("name", "")))
            elif result != "Pass":
                errored += 1

    errors = []
    if total < min_total:
        errors.append("only %d tests ran, expected at least %d" % (total, min_total))
    if failed:
        errors.append("%d tests failed" % failed)
    if errored:
        errors.append("%d assembly/test errors or unknown results" % errored)
    for test_type, name in skipped:
        if test_type not in allowed_classes:
            errors.append("skipped outside the allowed classes: %s" % name)
    summary = "total=%d failed=%d errors=%d skipped=%d" % (total, failed, errored, len(skipped))
    return errors, summary


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--report", required=True)
    parser.add_argument("--min-total", type=int, required=True)
    parser.add_argument("--allow-skip-class", action="append", default=[],
                        help="fully qualified test class whose tests may skip (repeatable)")
    args = parser.parse_args(argv)

    errors, summary = check(args.report, args.min_total, set(args.allow_skip_class))
    for message in errors:
        sys.stderr.write("check-cli-test-report: FAIL: " + message + "\n")
    if errors:
        if summary:
            sys.stderr.write("check-cli-test-report: " + summary + "\n")
        return 1
    print("check-cli-test-report: PASS " + summary)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
