#!/usr/bin/env python3
"""
Flaky Test Analysis Script

GAP-X04: Analyzes test failure patterns from CI logs and pytest reports
to identify and track flaky tests.

Usage:
    python scripts/analyze_flaky_tests.py [--ci-logs PATH] [--junit-reports PATH]
    python scripts/analyze_flaky_tests.py --report  # Generate markdown report
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any
from xml.etree import ElementTree as ET

logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
logger = logging.getLogger(__name__)

# Default paths
DEFAULT_CI_LOGS_DIR = Path(".buildlogs/ci")
DEFAULT_JUNIT_DIR = Path(".buildlogs/junit")
DEFAULT_REPORT_PATH = Path("tests/KNOWN_FLAKY.md")


@dataclass
class TestResult:
    """A single test execution result."""
    test_name: str
    file_path: str
    outcome: str  # passed, failed, skipped, error
    duration: float = 0.0
    error_message: str = ""
    timestamp: datetime = field(default_factory=datetime.now)
    run_id: str = ""


@dataclass
class FlakyTestInfo:
    """Aggregated information about a potentially flaky test."""
    test_name: str
    file_path: str
    total_runs: int = 0
    failures: int = 0
    passes: int = 0
    error_messages: list[str] = field(default_factory=list)
    
    @property
    def flakiness_score(self) -> float:
        """
        Calculate flakiness score (0-1).
        
        A test that fails 50% of the time has score 0.5 (most flaky).
        A test that always passes or always fails has score 0.
        """
        if self.total_runs < 2:
            return 0.0
        fail_rate = self.failures / self.total_runs
        # Flakiness is highest at 50% fail rate
        return 1 - abs(0.5 - fail_rate) * 2
    
    @property
    def is_flaky(self) -> bool:
        """A test is considered flaky if it has both passes and failures."""
        return self.passes > 0 and self.failures > 0


def parse_junit_xml(xml_path: Path) -> list[TestResult]:
    """Parse a JUnit XML report file."""
    results = []
    try:
        tree = ET.parse(xml_path)
        root = tree.getroot()
        
        # Handle both <testsuites> and <testsuite> root elements
        testsuites = root.findall(".//testsuite")
        if root.tag == "testsuite":
            testsuites = [root]
        
        run_id = xml_path.stem
        
        for testsuite in testsuites:
            for testcase in testsuite.findall("testcase"):
                name = testcase.get("name", "unknown")
                classname = testcase.get("classname", "")
                time_str = testcase.get("time", "0")
                
                try:
                    duration = float(time_str)
                except ValueError:
                    duration = 0.0
                
                # Determine outcome
                failure = testcase.find("failure")
                error = testcase.find("error")
                skipped = testcase.find("skipped")
                
                if failure is not None:
                    outcome = "failed"
                    error_message = failure.get("message", "") or failure.text or ""
                elif error is not None:
                    outcome = "error"
                    error_message = error.get("message", "") or error.text or ""
                elif skipped is not None:
                    outcome = "skipped"
                    error_message = ""
                else:
                    outcome = "passed"
                    error_message = ""
                
                results.append(TestResult(
                    test_name=name,
                    file_path=classname,
                    outcome=outcome,
                    duration=duration,
                    error_message=error_message[:500],  # Truncate
                    run_id=run_id
                ))
    except ET.ParseError as e:
        logger.warning(f"Failed to parse {xml_path}: {e}")
    except Exception as e:
        logger.warning(f"Error processing {xml_path}: {e}")
    
    return results


def parse_pytest_log(log_path: Path) -> list[TestResult]:
    """Parse pytest output log for test results."""
    results = []
    
    # Patterns for pytest output
    test_pattern = re.compile(
        r"^(tests/\S+\.py)::(\S+)\s+(PASSED|FAILED|SKIPPED|ERROR)"
    )
    rerun_pattern = re.compile(
        r"^(tests/\S+\.py)::(\S+)\s+RERUN"
    )
    
    try:
        with open(log_path, encoding="utf-8", errors="ignore") as f:
            for line in f:
                match = test_pattern.search(line)
                if match:
                    file_path, test_name, outcome = match.groups()
                    results.append(TestResult(
                        test_name=test_name,
                        file_path=file_path,
                        outcome=outcome.lower(),
                        run_id=log_path.stem
                    ))
                
                # Track reruns as failures
                rerun_match = rerun_pattern.search(line)
                if rerun_match:
                    file_path, test_name = rerun_match.groups()
                    results.append(TestResult(
                        test_name=test_name,
                        file_path=file_path,
                        outcome="rerun",
                        run_id=log_path.stem
                    ))
    except Exception as e:
        logger.warning(f"Error parsing {log_path}: {e}")
    
    return results


def aggregate_results(results: list[TestResult]) -> dict[str, FlakyTestInfo]:
    """Aggregate test results by test name."""
    tests: dict[str, FlakyTestInfo] = {}
    
    for result in results:
        key = f"{result.file_path}::{result.test_name}"
        
        if key not in tests:
            tests[key] = FlakyTestInfo(
                test_name=result.test_name,
                file_path=result.file_path
            )
        
        info = tests[key]
        info.total_runs += 1
        
        if result.outcome in ("passed",):
            info.passes += 1
        elif result.outcome in ("failed", "error", "rerun"):
            info.failures += 1
            if result.error_message and result.error_message not in info.error_messages:
                info.error_messages.append(result.error_message[:200])
    
    return tests


def find_flaky_tests(
    ci_logs_dir: Path | None = None,
    junit_dir: Path | None = None,
    min_runs: int = 2
) -> list[FlakyTestInfo]:
    """Find flaky tests from CI logs and JUnit reports."""
    all_results: list[TestResult] = []
    
    # Parse JUnit XML files
    if junit_dir and junit_dir.exists():
        for xml_file in junit_dir.glob("**/*.xml"):
            all_results.extend(parse_junit_xml(xml_file))
        logger.info(f"Parsed {len(all_results)} results from JUnit XML files")
    
    # Parse pytest logs
    if ci_logs_dir and ci_logs_dir.exists():
        for log_file in ci_logs_dir.glob("**/*.log"):
            all_results.extend(parse_pytest_log(log_file))
        for log_file in ci_logs_dir.glob("**/*.txt"):
            all_results.extend(parse_pytest_log(log_file))
        logger.info(f"Total {len(all_results)} test results collected")
    
    # Aggregate and filter
    aggregated = aggregate_results(all_results)
    
    flaky = [
        info for info in aggregated.values()
        if info.is_flaky and info.total_runs >= min_runs
    ]
    
    # Sort by flakiness score (most flaky first)
    flaky.sort(key=lambda x: x.flakiness_score, reverse=True)
    
    return flaky


def generate_markdown_report(flaky_tests: list[FlakyTestInfo], output_path: Path):
    """Generate a markdown report of flaky tests."""
    content = [
        "# Known Flaky Tests",
        "",
        "> **Auto-generated** by `scripts/analyze_flaky_tests.py`",
        f"> **Last updated**: {datetime.now().strftime('%Y-%m-%d %H:%M')}",
        "",
        "## Summary",
        "",
        f"Total flaky tests detected: **{len(flaky_tests)}**",
        "",
        "## Flaky Tests",
        "",
        "| Test | File | Runs | Pass Rate | Flakiness |",
        "|------|------|------|-----------|-----------|",
    ]
    
    for test in flaky_tests[:50]:  # Limit to top 50
        pass_rate = (test.passes / test.total_runs * 100) if test.total_runs > 0 else 0
        flakiness = test.flakiness_score * 100
        content.append(
            f"| `{test.test_name}` | `{test.file_path}` | "
            f"{test.total_runs} | {pass_rate:.0f}% | {flakiness:.0f}% |"
        )
    
    content.extend([
        "",
        "## Recommendations",
        "",
        "For each flaky test, consider:",
        "",
        "1. **Add `@pytest.mark.flaky(reruns=2)`** - Auto-retry on failure",
        "2. **Fix timing issues** - Add proper waits or mocks",
        "3. **Improve test isolation** - Ensure tests don't share state",
        "4. **Add to skip list** - If unfixable, skip with reason",
        "",
        "## Usage",
        "",
        "```bash",
        "# Re-run this analysis",
        "python scripts/analyze_flaky_tests.py --report",
        "",
        "# Run tests with auto-retry",
        "pytest tests/ --reruns 2 --reruns-delay 1",
        "```",
        "",
    ])
    
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(content), encoding="utf-8")
    logger.info(f"Report written to {output_path}")


def main():
    parser = argparse.ArgumentParser(description="Analyze flaky tests")
    parser.add_argument(
        "--ci-logs",
        type=Path,
        default=DEFAULT_CI_LOGS_DIR,
        help="Directory containing CI log files"
    )
    parser.add_argument(
        "--junit-reports",
        type=Path,
        default=DEFAULT_JUNIT_DIR,
        help="Directory containing JUnit XML reports"
    )
    parser.add_argument(
        "--report",
        action="store_true",
        help="Generate markdown report"
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_REPORT_PATH,
        help="Output path for markdown report"
    )
    parser.add_argument(
        "--min-runs",
        type=int,
        default=2,
        help="Minimum runs required to consider a test"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON"
    )
    
    args = parser.parse_args()
    
    flaky_tests = find_flaky_tests(
        ci_logs_dir=args.ci_logs,
        junit_dir=args.junit_reports,
        min_runs=args.min_runs
    )
    
    if not flaky_tests:
        logger.info("No flaky tests detected (or insufficient data)")
        # Create an empty report anyway
        if args.report:
            generate_markdown_report([], args.output)
        return 0
    
    logger.info(f"Found {len(flaky_tests)} flaky tests")
    
    if args.json:
        output = [
            {
                "test_name": t.test_name,
                "file_path": t.file_path,
                "total_runs": t.total_runs,
                "failures": t.failures,
                "passes": t.passes,
                "flakiness_score": t.flakiness_score,
            }
            for t in flaky_tests
        ]
        print(json.dumps(output, indent=2))
    elif args.report:
        generate_markdown_report(flaky_tests, args.output)
    else:
        # Print summary
        print("\nFlaky Tests Summary:")
        print("-" * 70)
        for test in flaky_tests[:20]:
            pass_rate = (test.passes / test.total_runs * 100) if test.total_runs > 0 else 0
            print(f"  {test.file_path}::{test.test_name}")
            print(f"    Runs: {test.total_runs}, Pass: {pass_rate:.0f}%, "
                  f"Flakiness: {test.flakiness_score*100:.0f}%")
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
