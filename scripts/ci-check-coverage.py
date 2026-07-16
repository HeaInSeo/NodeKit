#!/usr/bin/env python3
"""
Coverage gate for CI.

Reads every coverage.cobertura.xml under TestResults/ - one per test
project, since each project must now write to its own subdirectory (see
verify.yml's "Test with coverage" steps) - and enforces:
  - an overall line/branch coverage floor across all reports combined
    (summed raw counts - this is a genuine total across disjoint assemblies)
  - a higher floor for a fixed set of "core" reproducibility-path classes.
    A class whose source is compiled into more than one assembly (e.g.
    GrpcToolSpecClient.cs is <Compile Include>d into both NodeKit.Cli.csproj
    and NodeKit.csproj) shows up as a separate <class> entry per report, one
    per compiled copy - these are not fragments of one class to sum, they're
    independently-measured copies, so the gate uses whichever report shows
    the best (max) rate rather than diluting a well-covered copy with
    another project's near-zero incidental coverage of its own copy.

Historically this only looked at a single coverage file (whichever file
under TestResults/ had the newest mtime), because "dotnet test --solution"
wrote both projects' coverage to the exact same output path and one
silently overwrote the other. NodeKit.Cli.Tests almost always lost that
race in practice, which meant CLI-only core classes (SubmitCommand) were
never actually gated - nobody had connected the "Core class not found in
coverage report" failure to this root cause until it was reproduced
deterministically by running each project separately.

Deliberately written without f-strings/dataclasses/PEP604 type hints so
it also runs under old Python 3.6 interpreters, not just CI's modern one.
"""

import glob
import os
import sys
import xml.etree.ElementTree as ET


CORE_CLASSES = (
    "NodeKit.Validation.Recipes.RecipeValidationPipeline",
    "NodeKit.Authoring.Recipes.RecipeRenderer",
    "NodeKit.Cli.SubmitCommand",
    "NodeKit.Grpc.GrpcToolSpecClient",
    "NodeKit.Cli.HarborImageDigestResolver",
)


class Counts(object):
    def __init__(self, covered=0, valid=0):
        self.covered = covered
        self.valid = valid

    def add(self, other):
        self.covered += other.covered
        self.valid += other.valid

    def rate(self):
        return float(self.covered) / self.valid if self.valid else 1.0


def discover_reports(explicit_paths):
    if explicit_paths:
        return explicit_paths
    return sorted(glob.glob("TestResults/**/coverage.cobertura.xml", recursive=True))


def overall_counts(root):
    lines = Counts(int(root.get("lines-covered", "0")), int(root.get("lines-valid", "0")))
    branches = Counts(int(root.get("branches-covered", "0")), int(root.get("branches-valid", "0")))
    return lines, branches


def class_counts(root, class_name):
    classes = [c for c in root.iter("class") if c.get("name") == class_name]
    if not classes:
        return None

    lines = Counts()
    branches = Counts()
    for cls in classes:
        for line in cls.findall(".//line"):
            lines.valid += 1
            hits = int(line.get("hits", "0"))
            if hits > 0:
                lines.covered += 1

            if line.get("branch") == "True":
                condition = line.get("condition-coverage", "")
                # Format: "100% (2/2)" or "50% (1/2)" - the (covered/valid)
                # part is exact; the leading percentage is a derived,
                # rounded display value we don't use.
                open_idx = condition.find("(")
                close_idx = condition.find(")")
                if open_idx >= 0 and close_idx > open_idx:
                    paren = condition[open_idx + 1 : close_idx]
                    if "/" in paren:
                        covered_str, valid_str = paren.split("/", 1)
                        branches.covered += int(covered_str)
                        branches.valid += int(valid_str)

    return lines, branches


def main():
    explicit_paths = sys.argv[1:]
    minimum_line_rate = float(os.environ.get("MINIMUM_LINE_RATE", "0.1400"))
    minimum_branch_rate = float(os.environ.get("MINIMUM_BRANCH_RATE", "0.0900"))
    core_minimum_line_rate = float(os.environ.get("CORE_MINIMUM_LINE_RATE", "0.70"))
    core_minimum_branch_rate = float(os.environ.get("CORE_MINIMUM_BRANCH_RATE", "0.50"))

    report_paths = discover_reports(explicit_paths)
    if not report_paths:
        sys.stderr.write("No coverage.cobertura.xml files found.\n")
        return 1

    roots = []
    for path in report_paths:
        if not os.path.isfile(path):
            sys.stderr.write("coverage report not found: %s\n" % path)
            return 1
        try:
            roots.append((path, ET.parse(path).getroot()))
        except ET.ParseError as exc:
            sys.stderr.write("Could not parse %s: %s\n" % (path, exc))
            return 1

    print("Coverage reports (%d):" % len(roots))
    for path, _ in roots:
        print("  %s" % path)

    total_lines = Counts()
    total_branches = Counts()
    for _, root in roots:
        lines, branches = overall_counts(root)
        total_lines.add(lines)
        total_branches.add(branches)

    print("Combined line coverage: %.4f, required: %.4f" % (total_lines.rate(), minimum_line_rate))
    print("Combined branch coverage: %.4f, required: %.4f" % (total_branches.rate(), minimum_branch_rate))

    ok = True
    if total_lines.rate() < minimum_line_rate:
        sys.stderr.write("Line coverage is below threshold.\n")
        ok = False
    if total_branches.rate() < minimum_branch_rate:
        sys.stderr.write("Branch coverage is below threshold.\n")
        ok = False

    # 전체 커버리지 하한(위)은 낮게 잡혀 있어 회귀 방어력이 약하다. 재현성/제출
    # 경로의 핵심 클래스는 별도의 더 높은 기준으로 지킨다. 임계값은 실측치에
    # 약간의 여유를 둔 것이지 이상적인 목표치가 아니다.
    #
    # Some core classes' source is compiled into more than one assembly
    # (e.g. src/Grpc/GrpcToolSpecClient.cs is <Compile Include>d into both
    # NodeKit.Cli.csproj and NodeKit.csproj) — each report's <class> entry
    # for that name is a genuinely separate compiled copy, not a fragment
    # of the same one. Summing their raw counts would dilute a
    # well-covered copy with an unrelated project's near-zero coverage of
    # its own copy, which is misleading rather than "more thorough". Take
    # the best (max) rate observed across reports instead — within a
    # single report, class_counts already sums correctly across multiple
    # <class> entries for the same name (true partial-class fragments in
    # one assembly).
    for class_name in CORE_CLASSES:
        best_line_rate = None
        best_branch_rate = None
        found_in_any = False

        for path, root in roots:
            result = class_counts(root, class_name)
            if result is None:
                continue
            found_in_any = True
            lines, branches = result
            if best_line_rate is None or lines.rate() > best_line_rate:
                best_line_rate = lines.rate()
            if best_branch_rate is None or branches.rate() > best_branch_rate:
                best_branch_rate = branches.rate()

        if not found_in_any:
            sys.stderr.write("Core class not found in any coverage report: %s\n" % class_name)
            ok = False
            continue

        print(
            "Core class: %s (line-rate: %.4f, branch-rate: %.4f)"
            % (class_name, best_line_rate, best_branch_rate)
        )

        if best_line_rate < core_minimum_line_rate:
            sys.stderr.write(
                "Core class %s line coverage %.4f is below the core threshold %.4f.\n"
                % (class_name, best_line_rate, core_minimum_line_rate)
            )
            ok = False
        if best_branch_rate < core_minimum_branch_rate:
            sys.stderr.write(
                "Core class %s branch coverage %.4f is below the core threshold %.4f.\n"
                % (class_name, best_branch_rate, core_minimum_branch_rate)
            )
            ok = False

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
