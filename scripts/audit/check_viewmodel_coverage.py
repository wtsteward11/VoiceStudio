#!/usr/bin/env python3
"""
VoiceStudio ViewModel Test Coverage Checker

Verifies that ViewModels have corresponding test files.
Reports coverage percentage and lists uncovered ViewModels.

Usage:
    python scripts/check_viewmodel_coverage.py
    python scripts/check_viewmodel_coverage.py --threshold 70
    python scripts/check_viewmodel_coverage.py --json

Exit codes:
    0: Coverage meets threshold
    1: Coverage below threshold or error
"""

import argparse
import json
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path


@dataclass
class CoverageReport:
    """Coverage report data."""
    timestamp: str = field(default_factory=lambda: datetime.now().isoformat())
    viewmodel_count: int = 0
    test_count: int = 0
    coverage_percent: float = 0.0
    covered: list[str] = field(default_factory=list)
    uncovered: list[str] = field(default_factory=list)
    panel_viewmodel_count: int = 0
    panel_test_count: int = 0
    panel_coverage_percent: float = 0.0

    def to_dict(self) -> dict:
        return {
            "timestamp": self.timestamp,
            "viewmodel_count": self.viewmodel_count,
            "test_count": self.test_count,
            "coverage_percent": self.coverage_percent,
            "covered": self.covered,
            "uncovered": self.uncovered,
            "panel_viewmodel_count": self.panel_viewmodel_count,
            "panel_test_count": self.panel_test_count,
            "panel_coverage_percent": self.panel_coverage_percent
        }


def get_project_root() -> Path:
    """Get the project root directory."""
    return Path(__file__).parent.parent.resolve()


def find_viewmodels(viewmodels_dir: Path) -> set[str]:
    """Find all ViewModel files in the given directory."""
    viewmodels = set()

    if not viewmodels_dir.exists():
        return viewmodels

    for path in viewmodels_dir.glob("*ViewModel.cs"):
        name = path.stem
        # Skip base classes and utilities
        if name in ("BaseViewModel", "ViewModelLocator", "ViewModelContext"):
            continue
        viewmodels.add(name)

    return viewmodels


def find_panel_viewmodels(panels_dir: Path) -> set[str]:
    """Find all ViewModel files in the Panels directory."""
    viewmodels = set()

    if not panels_dir.exists():
        return viewmodels

    for path in panels_dir.glob("*ViewModel.cs"):
        viewmodels.add(path.stem)

    return viewmodels


def find_tests(tests_dir: Path) -> set[str]:
    """Find all test files and extract the ViewModel names they test."""
    tested = set()

    if not tests_dir.exists():
        return tested

    for path in tests_dir.glob("*ViewModelTests.cs"):
        # Remove "Tests" suffix to get the ViewModel name
        name = path.stem.replace("Tests", "")
        tested.add(name)

    return tested


def calculate_coverage(viewmodels: set[str], tests: set[str]) -> tuple[set[str], set[str], float]:
    """Calculate coverage and return covered, uncovered, and percentage."""
    covered = viewmodels & tests
    uncovered = viewmodels - tests

    if len(viewmodels) == 0:
        return covered, uncovered, 100.0

    percent = (len(covered) / len(viewmodels)) * 100
    return covered, uncovered, percent


def generate_table_report(report: CoverageReport) -> str:
    """Generate a simple table report for CI output."""
    lines = [
        "ViewModel Test Coverage Report",
        "=" * 60,
        "",
        f"Coverage: {report.test_count}/{report.viewmodel_count} ({report.coverage_percent:.1f}%)",
        f"Panel Coverage: {report.panel_test_count}/{report.panel_viewmodel_count} ({report.panel_coverage_percent:.1f}%)",
        "",
    ]

    if report.uncovered:
        lines.extend([
            "Uncovered ViewModels:",
            "-" * 40,
        ])
        for vm in sorted(report.uncovered)[:20]:  # Limit to 20 for readability
            lines.append(f"  [ ] {vm}")
        if len(report.uncovered) > 20:
            lines.append(f"  ... and {len(report.uncovered) - 20} more")
        lines.append("")

    return "\n".join(lines)


def generate_markdown_report(report: CoverageReport) -> str:
    """Generate a markdown report."""
    lines = [
        "# ViewModel Test Coverage Report",
        "",
        f"**Generated:** {report.timestamp}",
        "",
        "## Summary",
        "",
        "| Category | ViewModels | Tested | Coverage |",
        "|----------|------------|--------|----------|",
        f"| Main ViewModels | {report.viewmodel_count} | {report.test_count} | {report.coverage_percent:.1f}% |",
        f"| Panel ViewModels | {report.panel_viewmodel_count} | {report.panel_test_count} | {report.panel_coverage_percent:.1f}% |",
        "",
    ]

    if report.covered:
        lines.extend([
            "## Covered ViewModels",
            "",
        ])
        for vm in sorted(report.covered):
            lines.append(f"- [x] {vm}")
        lines.append("")

    if report.uncovered:
        lines.extend([
            "## Uncovered ViewModels (Need Tests)",
            "",
        ])
        for vm in sorted(report.uncovered):
            lines.append(f"- [ ] {vm}")
        lines.append("")

    # Priority recommendations
    lines.extend([
        "## Priority Test Recommendations",
        "",
    ])

    # Identify high-priority uncovered ViewModels
    priority_keywords = ["Settings", "Library", "Timeline", "Recording", "Training",
                         "Synthesis", "Transcribe", "Profile", "Model", "Engine"]
    priority_uncovered = [vm for vm in report.uncovered
                          if any(kw in vm for kw in priority_keywords)]

    if priority_uncovered:
        lines.append("High-priority ViewModels without tests:")
        for vm in sorted(priority_uncovered)[:10]:
            lines.append(f"1. `{vm}` - Core functionality")
    else:
        lines.append("All high-priority ViewModels have tests!")

    lines.extend([
        "",
        "---",
        "*Generated by check_viewmodel_coverage.py*"
    ])

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check ViewModel test coverage"
    )
    parser.add_argument(
        "--threshold", "-t",
        type=float,
        default=0.0,
        help="Minimum coverage percentage required (default: 0, no threshold)"
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Output file path (default: print to stdout)"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON instead of markdown"
    )
    parser.add_argument(
        "--format", "-f",
        type=str,
        choices=["markdown", "json", "table"],
        default="markdown",
        help="Output format (default: markdown)"
    )

    args = parser.parse_args()

    # --json is shorthand for --format json
    if args.json:
        args.format = "json"

    project_root = get_project_root()

    # Paths
    viewmodels_dir = project_root / "src" / "VoiceStudio.App" / "ViewModels"
    panels_dir = project_root / "src" / "VoiceStudio.App" / "Views" / "Panels"
    tests_dir = project_root / "src" / "VoiceStudio.App.Tests" / "ViewModels"

    # Find ViewModels
    main_viewmodels = find_viewmodels(viewmodels_dir)
    panel_viewmodels = find_panel_viewmodels(panels_dir)
    all_viewmodels = main_viewmodels | panel_viewmodels

    # Find tests
    tests = find_tests(tests_dir)

    # Calculate coverage
    covered, uncovered, coverage_percent = calculate_coverage(all_viewmodels, tests)

    # Calculate panel-specific coverage
    panel_covered = panel_viewmodels & tests
    panel_coverage = (len(panel_covered) / len(panel_viewmodels) * 100) if panel_viewmodels else 100.0

    # Build report
    report = CoverageReport(
        viewmodel_count=len(all_viewmodels),
        test_count=len(tests),
        coverage_percent=coverage_percent,
        covered=sorted(covered),
        uncovered=sorted(uncovered),
        panel_viewmodel_count=len(panel_viewmodels),
        panel_test_count=len(panel_covered),
        panel_coverage_percent=panel_coverage
    )

    # Generate output based on format
    if args.format == "json":
        output = json.dumps(report.to_dict(), indent=2)
    elif args.format == "table":
        output = generate_table_report(report)
    else:  # markdown
        output = generate_markdown_report(report)

    # Also write JSON report to buildlogs for artifact upload
    buildlogs_dir = project_root / ".buildlogs"
    buildlogs_dir.mkdir(exist_ok=True)
    json_report_path = buildlogs_dir / "viewmodel-coverage.json"
    json_report_path.write_text(json.dumps(report.to_dict(), indent=2), encoding='utf-8')

    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding='utf-8')
        print(f"Report written to: {args.output}", file=sys.stderr)
    else:
        print(output)

    # Summary to stderr
    print(f"\nCoverage: {len(covered)}/{len(all_viewmodels)} ViewModels ({coverage_percent:.1f}%)", file=sys.stderr)
    print(f"Panel Coverage: {len(panel_covered)}/{len(panel_viewmodels)} ({panel_coverage:.1f}%)", file=sys.stderr)

    # Check threshold
    if args.threshold > 0 and coverage_percent < args.threshold:
        print(f"\nFAIL: Coverage {coverage_percent:.1f}% is below threshold {args.threshold}%", file=sys.stderr)
        return 1

    if args.threshold > 0:
        print(f"\nPASS: Coverage {coverage_percent:.1f}% meets threshold {args.threshold}%", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
